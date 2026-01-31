using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Collections.Concurrent;
using BkyhBot.BotAction;
using BkyhBot.Class;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BkyhBot.BotConnect;

public class BotConnect
{
	private HttpListener? _listener;
	private CancellationTokenSource? _cts;
	private readonly ConcurrentDictionary<string, WebSocket> _activeBots = new();
	private static BotConnect? _instance;

	public Config Config { get; private set; }
	public List<PlugMessage> PlugMessageList { get; set; } = new();
	public BotActionSender Sender { get; private set; }
	public static BotConnect Instance => _instance!;

	#region 事件定义

	// 原有事件保持不变，旧插件依然订阅这些
	public event Action<GroupMessageEvent>? OnGroupMessageReceived;
	public event Action<PrivateMessageEvent>? OnPrivateMessageReceived;
	public event Action<NoticeEvent>? OnNoticeReceived;
	public event Action<RequestEvent>? OnRequestReceived;
	public event Action<MetaEvent>? OnMetaEventReceived;
	public event Action<JObject, string>? OnOtherEventReceived;

	public event Action<string>? OnLog;
	public event Action? BotOnline;
	public event Action<BotConnect>? StartFinish;

	// [新增] API 响应事件 (Additive Change)
	// 只有 GroupManager 这种需要查数据的插件才会用，其他插件不订阅也无所谓
	public event Action<JObject>? OnApiResponseReceived;

	#endregion

	public BotConnect() : this(LoadOrInitConfig())
	{
	}

	public BotConnect(Config config)
	{
		if (_instance != null) return;
		_instance = this;
		Config = config ?? throw new ArgumentNullException(nameof(config));
		if (string.IsNullOrWhiteSpace(Config.Url)) throw new ArgumentException("监听 URL 不能为空");
		if (!Config.Url.EndsWith("/")) Config.Url += "/";
		string initialId = Config.BotQq ?? "";
		Sender = new BotActionSender(_activeBots, Log, initialId);
	}

	// ... (LoadOrInitConfig, Start, Stop, AcceptConnectionsLoop, HandleWebsocketHandshake, ReceiveMessageLoop 保持原样) ...
	// 为了节省篇幅，这里略过这些未修改的方法，请保留你原文件中的内容。
	// 务必保留 ReceiveMessageLoop，它会调用下面的 ProcessReceivedJson。

	private static Config LoadOrInitConfig()
	{
		// 保持原样，直接使用你现有的代码
		string configPath = Path.Combine("Configs", "Config.json");
		Console.WriteLine($"[系统] 配置文件路径: {Path.GetFullPath(configPath)}");
		if (!File.Exists(configPath))
		{
			try
			{
				string dir = Path.GetDirectoryName(configPath)!;
				if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
				var defaultConfig = new Config
				{
					Url = "http://127.0.0.1:3001/", BotQq = "123456789", MasterQq = "987654321", PlugConfigPath = "Plugins/",
					EnableAllGroups = false, GroupWhiteList = new List<string>(), GroupBlackList = new List<string>(),
					PrivateWhiteList = new List<string>(), PrivateBlackList = new List<string>()
				};
				string json = JsonConvert.SerializeObject(defaultConfig, Formatting.Indented);
				File.WriteAllText(configPath, json);
				throw new FileNotFoundException($"[初次运行] 已生成配置文件：{configPath}");
			}
			catch (Exception ex) when (ex is not FileNotFoundException)
			{
				throw new Exception($"无法创建配置文件: {ex.Message}");
			}
		}

		try
		{
			return JsonConvert.DeserializeObject<Config>(File.ReadAllText(configPath)) ?? new Config();
		}
		catch (Exception ex)
		{
			throw new Exception($"配置文件读取失败: {ex.Message}");
		}
	}

	public async Task Start()
	{
		if (_listener?.IsListening == true) return;
		_listener = new HttpListener();
		_listener.Prefixes.Add(Config.Url);
		try
		{
			_listener.Start();
			_cts = new CancellationTokenSource();
			Log($"[系统] 监听启动 | {Config.Url}");
			_ = AcceptConnectionsLoop(_cts.Token);
			StartFinish?.Invoke(this);
		}
		catch (HttpListenerException ex)
		{
			Log($"[启动失败] 端口被占用: {ex.Message}");
			throw;
		}
	}

	public void Stop()
	{
		_cts?.Cancel();
		_listener?.Stop();
		foreach (var pair in _activeBots)
		{
			try
			{
				pair.Value.Abort();
				pair.Value.Dispose();
			}
			catch
			{
			}
		}

		_activeBots.Clear();
		Log("[系统] 服务已停止");
	}

	private async Task AcceptConnectionsLoop(CancellationToken ct)
	{
		while (!ct.IsCancellationRequested && _listener != null)
		{
			try
			{
				var context = await _listener.GetContextAsync();
				if (context.Request.IsWebSocketRequest) _ = HandleWebsocketHandshake(context, ct);
				else
				{
					context.Response.StatusCode = 400;
					context.Response.Close();
				}
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				if (_listener?.IsListening == true) Log($"[监听异常] {ex.Message}");
			}
		}
	}

	private async Task HandleWebsocketHandshake(HttpListenerContext context, CancellationToken ct)
	{
		string botId = "";
		WebSocket? socket = null;
		try
		{
			if (!string.IsNullOrEmpty(Config.Token))
			{
				string? auth = context.Request.Headers["Authorization"];
				if (string.IsNullOrEmpty(auth))
				{
					string? queryToken = context.Request.QueryString["access_token"];
					if (!string.IsNullOrEmpty(queryToken)) auth = "Bearer " + queryToken;
				}

				if (string.IsNullOrEmpty(auth) || auth != $"Bearer {Config.Token}")
				{
					Log("[拒绝] Token 校验失败");
					context.Response.StatusCode = 401;
					context.Response.Close();
					return;
				}
			}

			if (context.Request.Headers["X-Self-ID"] is string id && !string.IsNullOrEmpty(id)) botId = id;
			else botId = "Unknown_" + Guid.NewGuid().ToString("N")[..6];

			if (!string.IsNullOrEmpty(Config.BotQq) && botId != Config.BotQq)
			{
				Log($"[拒绝] ID不匹配 (期望:{Config.BotQq}, 实际:{botId})");
				context.Response.StatusCode = 403;
				context.Response.Close();
				return;
			}

			var wsContext = await context.AcceptWebSocketAsync(null);
			socket = wsContext.WebSocket;
			_activeBots[botId] = socket;
			Sender = new BotActionSender(_activeBots, Log, botId); // 这里的 Sender 已经是新的加锁版了
			Log($"[连接] 机器人 {botId} 已接入");
			BotOnline?.Invoke();
			await ReceiveMessageLoop(socket, botId, ct);
		}
		catch (Exception ex)
		{
			Log($"[连接异常] Bot:{botId} - {ex.Message}");
		}
		finally
		{
			if (socket != null)
			{
				_activeBots.TryRemove(botId, out _);
				socket.Dispose();
				Log($"[断开] 机器人 {botId} 连接已关闭");
			}
		}
	}

	private async Task ReceiveMessageLoop(WebSocket socket, string botId, CancellationToken ct)
	{
		var buffer = new byte[1024 * 128];
		while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
		{
			try
			{
				using var ms = new MemoryStream();
				WebSocketReceiveResult result;
				do
				{
					result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
					if (result.MessageType == WebSocketMessageType.Close) return;
					ms.Write(buffer, 0, result.Count);
				} while (!result.EndOfMessage);

				if (result.MessageType == WebSocketMessageType.Text)
				{
					ms.Seek(0, SeekOrigin.Begin);
					using var reader = new StreamReader(ms, Encoding.UTF8);
					string json = await reader.ReadToEndAsync();
					// 这里不需要修改，因为它调用的是 ProcessReceivedJson
					_ = Task.Run(() => ProcessReceivedJson(json, botId), ct);
				}
			}
			catch
			{
				break;
			}
		}
	}

	/// <summary>
	/// [修复] 使用 Task.Run 确保完全异步，防止主程序因插件耗时操作而卡死
	/// </summary>
	private void ProcessReceivedJson(string json, string botId)
	{
		Task.Run(() =>
		{
			try
			{
				var jsonObj = JObject.Parse(json);
				string? postType = jsonObj["post_type"]?.ToString();

				// 1. 处理 API 响应 (GroupManager 需要用到)
				if (string.IsNullOrEmpty(postType))
				{
					if (jsonObj.ContainsKey("echo")) OnApiResponseReceived?.Invoke(jsonObj);
					return;
				}

				// 2. 正常分发事件 (旧插件正常接收)
				switch (postType)
				{
					case "message":
						HandleMessage(jsonObj);
						break;
					case "notice":
						var noticeEvent = jsonObj.ToObject<NoticeEvent>();
						if (noticeEvent != null) OnNoticeReceived?.Invoke(noticeEvent);
						else OnOtherEventReceived?.Invoke(jsonObj, botId);
						break;
					case "request":
						var requestEvent = jsonObj.ToObject<RequestEvent>();
						if (requestEvent != null) OnRequestReceived?.Invoke(requestEvent);
						else OnOtherEventReceived?.Invoke(jsonObj, botId);
						break;
					case "meta_event":
						var metaEvent = jsonObj.ToObject<MetaEvent>();
						if (metaEvent != null) OnMetaEventReceived?.Invoke(metaEvent);
						break;
					default:
						OnOtherEventReceived?.Invoke(jsonObj, botId);
						break;
				}
			}
			catch (Exception ex)
			{
				Log($"[数据处理异常] {ex.Message}");
			}
		});
	}

	private void HandleMessage(JObject jsonObj)
	{
		// 保持原样，无需修改
		string? msgType = jsonObj["message_type"]?.ToString();
		if (msgType == "group")
		{
			var groupEvent = jsonObj.ToObject<GroupMessageEvent>();
			if (groupEvent == null) return;
			if (Config.GroupBlackList != null && Config.GroupBlackList.Contains(groupEvent.GroupId)) return;
			if (!Config.EnableAllGroups)
			{
				if (Config.GroupWhiteList == null || !Config.GroupWhiteList.Contains(groupEvent.GroupId)) return;
			}

			OnGroupMessageReceived?.Invoke(groupEvent);
		}
		else if (msgType == "private")
		{
			var privateEvent = jsonObj.ToObject<PrivateMessageEvent>();
			if (privateEvent == null) return;
			if (Config.PrivateBlackList != null && Config.PrivateBlackList.Contains(privateEvent.UserId)) return;
			if (Config.PrivateWhiteList != null && Config.PrivateWhiteList.Count > 0)
			{
				if (!Config.PrivateWhiteList.Contains(privateEvent.UserId)) return;
			}

			OnPrivateMessageReceived?.Invoke(privateEvent);
		}
		else
		{
			OnOtherEventReceived?.Invoke(jsonObj, jsonObj["self_id"]?.ToString() ?? "");
		}
	}

	private void Log(string msg) => OnLog?.Invoke(msg);
}