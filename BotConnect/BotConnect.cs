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
	#region 私有字段

	private HttpListener? _listener;
	private CancellationTokenSource? _cts;
	private readonly ConcurrentDictionary<string, WebSocket> _activeBots = new();
	private static BotConnect? _instance;

	#endregion

	#region 公共属性

	public Config Config { get; private set; }
	public List<PlugMessage> PlugMessageList { get; set; } = new();
	public BotActionSender Sender { get; private set; }
	public static BotConnect Instance => _instance!;

	#endregion

	#region 事件定义 (NapCat/OneBot11 全事件支持)

	// 消息事件
	public event Action<GroupMessageEvent>? OnGroupMessageReceived;
	public event Action<PrivateMessageEvent>? OnPrivateMessageReceived;

	// 通知事件 (群成员变动、戳一戳、荣誉变更等)
	public event Action<NoticeEvent>? OnNoticeReceived;

	// 请求事件 (加好友、加群申请)
	public event Action<RequestEvent>? OnRequestReceived;

	// 元事件 (心跳、生命周期)
	public event Action<MetaEvent>? OnMetaEventReceived;

	// 兜底事件 (处理未知类型的 JSON)
	public event Action<JObject, string>? OnOtherEventReceived;

	// 系统事件
	public event Action<string>? OnLog;
	public event Action? BotOnline;
	public event Action<BotConnect>? StartFinish;

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

	/// <summary>
	/// 加载或初始化配置
	/// </summary>
	private static Config LoadOrInitConfig()
	{
		string configPath = Path.Combine("Configs", "Config.json");
		Console.WriteLine($"[系统] 配置文件路径: {Path.GetFullPath(configPath)}");

		// 如果文件不存在，自动创建默认配置
		if (!File.Exists(configPath))
		{
			try
			{
				string dir = Path.GetDirectoryName(configPath)!;
				if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
					Directory.CreateDirectory(dir);

				var defaultConfig = new Config
				{
					Url = "http://127.0.0.1:3001/",
					BotQq = "123456789",
					MasterQq = "987654321",
					PlugConfigPath = "Plugins/",

					// [新功能] 全群响应开关
					EnableAllGroups = false, // 默认关闭，只响应白名单

					// [新功能] 黑白名单
					GroupWhiteList = new List<string>(),
					GroupBlackList = new List<string>(),
					PrivateWhiteList = new List<string>(),
					PrivateBlackList = new List<string>()
				};

				string json = JsonConvert.SerializeObject(defaultConfig, Formatting.Indented);
				File.WriteAllText(configPath, json);

				throw new FileNotFoundException(
					$"[初次运行] 已生成配置文件：{Path.GetFullPath(configPath)}\n请修改配置后重启程序。"
				);
			}
			catch (Exception ex) when (ex is not FileNotFoundException)
			{
				throw new Exception($"无法创建配置文件: {ex.Message}");
			}
		}

		// 读取已有配置
		try
		{
			string fileContent = File.ReadAllText(configPath);
			return JsonConvert.DeserializeObject<Config>(fileContent) ?? new Config();
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
			// 1. Token 鉴权
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

			// 2. 获取 Bot ID
			if (context.Request.Headers["X-Self-ID"] is string id && !string.IsNullOrEmpty(id)) botId = id;
			else botId = "Unknown_" + Guid.NewGuid().ToString("N")[..6];

			// 3. Bot ID 校验
			if (!string.IsNullOrEmpty(Config.BotQq) && botId != Config.BotQq)
			{
				Log($"[拒绝] ID不匹配 (期望:{Config.BotQq}, 实际:{botId})");
				context.Response.StatusCode = 403;
				context.Response.Close();
				return;
			}

			// 4. 建立连接
			var wsContext = await context.AcceptWebSocketAsync(null);
			socket = wsContext.WebSocket;
			_activeBots[botId] = socket;

			// 更新 ActionSender
			Sender = new BotActionSender(_activeBots, Log, botId);

			Log($"[连接] 机器人 {botId} 已接入");
			BotOnline?.Invoke();

			// 进入接收循环
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

					// 异步处理消息，不阻塞接收循环
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
	/// 处理接收到的 JSON 数据并分发事件
	/// </summary>
	private void ProcessReceivedJson(string json, string botId)
	{
		try
		{
			var jsonObj = JObject.Parse(json);
			string? postType = jsonObj["post_type"]?.ToString();

			// 如果没有 post_type，可能是 API 响应 (echo)，暂时忽略
			if (string.IsNullOrEmpty(postType)) return;

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
	}

	/// <summary>
	/// 处理消息并进行权限过滤 (全群响应开关 + 黑白名单)
	/// </summary>
	private void HandleMessage(JObject jsonObj)
	{
		string? msgType = jsonObj["message_type"]?.ToString();

		if (msgType == "group")
		{
			var groupEvent = jsonObj.ToObject<GroupMessageEvent>();
			if (groupEvent == null) return;

			// 1. 黑名单检查 (最高优先级：在黑名单里直接拦截)
			if (Config.GroupBlackList != null && Config.GroupBlackList.Contains(groupEvent.GroupId))
				return;

			// 2. 权限检查 (全群开关 OR 白名单)
			if (!Config.EnableAllGroups)
			{
				// 如果【没开启全群】，则必须在【白名单】里
				if (Config.GroupWhiteList == null || !Config.GroupWhiteList.Contains(groupEvent.GroupId))
					return; // 既没开全群，又不在白名单 -> 拦截
			}
			// 如果 EnableAllGroups 为 true，则跳过白名单检查，直接响应 (前提是不在黑名单)

			// 通过所有检查，触发事件
			OnGroupMessageReceived?.Invoke(groupEvent);
		}
		else if (msgType == "private")
		{
			var privateEvent = jsonObj.ToObject<PrivateMessageEvent>();
			if (privateEvent == null) return;

			// 1. 黑名单检查
			if (Config.PrivateBlackList != null && Config.PrivateBlackList.Contains(privateEvent.UserId))
				return;

			// 2. 白名单检查 (私聊目前逻辑：如果白名单有内容，则必须在白名单里)
			if (Config.PrivateWhiteList != null && Config.PrivateWhiteList.Count > 0)
			{
				if (!Config.PrivateWhiteList.Contains(privateEvent.UserId))
					return;
			}

			OnPrivateMessageReceived?.Invoke(privateEvent);
		}
		else
		{
			// 未知类型
			OnOtherEventReceived?.Invoke(jsonObj, jsonObj["self_id"]?.ToString() ?? "");
		}
	}

	private void Log(string msg) => OnLog?.Invoke(msg);
}