using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Collections.Concurrent;
using BkyhBot.BotAction;
using BkyhBot.Class;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using BkyhBot.Web;
using System.Threading; // [新增] 确保引用，用于 SemaphoreSlim 和 CancellationToken

namespace BkyhBot.BotConnect;

public class BotConnect
{
	private HttpListener? _listener;
	private CancellationTokenSource? _cts;
	private readonly ConcurrentDictionary<string, WebSocket> _activeBots = new();
	private static BotConnect? _instance;

	// 锁：防止并发重启
	private readonly SemaphoreSlim _lifecycleLock = new(1, 1);

	public Config Config { get; private set; }
	public List<PlugMessage> PlugMessageList { get; set; } = new();
	public BotActionSender Sender { get; private set; }
	public static BotConnect Instance => _instance!;

	#region 事件定义

	public event Action<GroupMessageEvent>? OnGroupMessageReceived;
	public event Action<PrivateMessageEvent>? OnPrivateMessageReceived;
	public event Action<NoticeEvent>? OnNoticeReceived;
	public event Action<RequestEvent>? OnRequestReceived;
	public event Action<MetaEvent>? OnMetaEventReceived;
	public event Action<JObject, string>? OnOtherEventReceived;
	public event Action<string>? OnLog;
	public event Action? BotOnline;
	public event Action<BotConnect>? StartFinish;
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

		Sender = new BotActionSender(_activeBots, Log, Config.BotQq ?? "");

		// 启动网页控制台
		try
		{
			_ = WebDashboard.StartAsync(Config);
			Log($"[Web] 网页后台启动成功: {Config.WebDashboardUrl}");
		}
		catch (Exception ex)
		{
			Log($"[Web] 网页后台启动失败: {ex.Message}");
		}
	}

	private static Config LoadOrInitConfig()
	{
		string configPath = Path.Combine("Configs", "Config.json");
		if (!File.Exists(configPath))
		{
			string dir = Path.GetDirectoryName(configPath)!;
			if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
			var defaultConfig = new Config
			{
				Url = "http://127.0.0.1:3001/", BotQq = "123456789", MasterQq = "987654321", PlugConfigPath = "Plugins/",
				EnableAllGroups = false, GroupWhiteList = new List<string>(), GroupBlackList = new List<string>(),
				PrivateWhiteList = new List<string>(), PrivateBlackList = new List<string>(),
				WebDashboardUrl = "http://*:5000", WebAdminSecret = "123456"
			};
			File.WriteAllText(configPath, JsonConvert.SerializeObject(defaultConfig, Formatting.Indented));
			throw new FileNotFoundException($"[初次运行] 已生成配置文件：{configPath}");
		}

		return JsonConvert.DeserializeObject<Config>(File.ReadAllText(configPath)) ?? new Config();
	}

	public async Task Start()
	{
		await _lifecycleLock.WaitAsync();
		try
		{
			if (_listener?.IsListening == true) return;

			_listener = new HttpListener();
			_listener.Prefixes.Add(Config.Url);
			_listener.Start();

			_cts = new CancellationTokenSource();
			Log($"[系统] 监听启动成功 | 端口: {Config.Url}");

			_ = AcceptConnectionsLoop(_cts.Token);
			StartFinish?.Invoke(this);
		}
		catch (HttpListenerException ex)
		{
			Log($"[启动失败] 端口可能被占用: {ex.Message}");
			throw;
		}
		finally
		{
			_lifecycleLock.Release();
		}
	}

	public void Stop()
	{
		try
		{
			_cts?.Cancel();
			_cts?.Dispose();
		}
		catch
		{
		}

		_cts = null;

		try
		{
			if (_listener != null)
			{
				_listener.Close();
				_listener = null;
			}
		}
		catch (Exception ex)
		{
			Log($"[停止异常] {ex.Message}");
		}

		foreach (var pair in _activeBots)
		{
			try
			{
				pair.Value.Dispose();
			}
			catch
			{
			}
		}

		_activeBots.Clear();
		Log("[系统] 服务已停止，资源已释放。");
	}

	public async Task Restart()
	{
		await _lifecycleLock.WaitAsync();
		try
		{
			Log("[系统] 正在执行重启序列... (ง •_•)ง");

			try
			{
				_cts?.Cancel();
				_listener?.Close();
				foreach (var b in _activeBots) b.Value.Dispose();
				_activeBots.Clear();
			}
			catch
			{
			}

			_listener = null;

			Log("[系统] 已断开连接，正在等待端口释放...");

			int retryCount = 0;
			const int maxRetries = 5;
			bool success = false;

			while (retryCount < maxRetries)
			{
				retryCount++;
				await Task.Delay(1500);

				try
				{
					_listener = new HttpListener();
					_listener.Prefixes.Add(Config.Url);
					_listener.Start();

					_cts = new CancellationTokenSource();
					_ = AcceptConnectionsLoop(_cts.Token);

					success = true;
					Log($"[系统] 第 {retryCount} 次尝试启动成功！欢迎回来~");
					break;
				}
				catch (Exception ex)
				{
					Log($"[系统] 第 {retryCount} 次启动失败 (端口可能仍被占用): {ex.Message}");
					try
					{
						_listener?.Close();
					}
					catch
					{
					}

					_listener = null;
				}
			}

			if (!success)
			{
				Log($"[系统] ❌ 严重错误: 重试 {maxRetries} 次后仍无法启动。请检查是否有其他程序占用了端口。");
			}
		}
		finally
		{
			_lifecycleLock.Release();
		}
	}

	private async Task AcceptConnectionsLoop(CancellationToken ct)
	{
		while (!ct.IsCancellationRequested && _listener != null)
		{
			try
			{
				var context = await _listener.GetContextAsync();
				if (context.Request.IsWebSocketRequest)
					_ = HandleWebsocketHandshake(context, ct);
				else
				{
					context.Response.StatusCode = 400;
					context.Response.Close();
				}
			}
			catch (Exception ex)
			{
				if (!ct.IsCancellationRequested)
				{
					if (ex is ObjectDisposedException || ex is HttpListenerException)
						break;

					Log($"[监听循环异常] {ex.Message}");
				}
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
			Sender = new BotActionSender(_activeBots, Log, botId);
			Log($"[连接] 机器人 {botId} 已接入");
			BotOnline?.Invoke();

			// [关键调用] 这里调用 ReceiveMessageLoop
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

	// [关键修复] 确保这个方法存在且在 BotConnect 类内部！
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
					_ = Task.Run(() => ProcessReceivedJson(json, botId), ct);
				}
			}
			catch
			{
				break;
			}
		}
	}

	private void ProcessReceivedJson(string json, string botId)
	{
		Task.Run(() =>
		{
			try
			{
				var jsonObj = JObject.Parse(json);
				string? postType = jsonObj["post_type"]?.ToString();

				if (string.IsNullOrEmpty(postType))
				{
					if (jsonObj.ContainsKey("echo")) OnApiResponseReceived?.Invoke(jsonObj);
					return;
				}

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
		string? msgType = jsonObj["message_type"]?.ToString();
		string content = jsonObj["raw_message"]?.ToString() ?? "";

		if (msgType == "group")
		{
			var groupEvent = jsonObj.ToObject<GroupMessageEvent>();
			if (groupEvent == null) return;
			if (Config.GroupBlackList != null && Config.GroupBlackList.Contains(groupEvent.GroupId)) return;
			if (!Config.EnableAllGroups)
			{
				if (Config.GroupWhiteList == null || !Config.GroupWhiteList.Contains(groupEvent.GroupId)) return;
			}

			Log($"[群聊] 群[{groupEvent.GroupId}] 用户[{groupEvent.UserId}]: {content}");
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

			Log($"[私聊] 用户[{privateEvent.UserId}]: {content}");
			OnPrivateMessageReceived?.Invoke(privateEvent);
		}
		else
		{
			OnOtherEventReceived?.Invoke(jsonObj, jsonObj["self_id"]?.ToString() ?? "");
		}
	}

	private void Log(string msg)
	{
		OnLog?.Invoke(msg);
		WebDashboard.AddLog(msg);
		Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {msg}");
	}
}