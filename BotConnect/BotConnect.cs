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
	private HttpListener _listener;
	private CancellationTokenSource _cts;
	private readonly ConcurrentDictionary<long, WebSocket> _activeBots = new();

	// 【修改】配置现在是只读的，在构造函数里定死
	private readonly Config _config;

	public Config Config => _config;

	//注册插件信息列表
	public List<PlugMessage> PlugMessageList { get; set; } = new();

	// ================== 公共属性 ==================
	public BotActionSender Sender { get; private set; }

	// ================== 事件 ==================
	public event Action<GroupMessageEvent>? OnGroupMessageReceived;
	public event Action<JObject, long>? OnOtherEventReceived;
	public event Action<string>? OnLog;
	public event Action BotOnline;
	public event Action<BotConnect> StartFinish;

	//单例
	private static BotConnect? _instance;
	public static BotConnect Instance => _instance;
	/// <summary>
	/// 【修改】构造函数，必须传入 Config
	/// </summary>
	public BotConnect(Config config)
	{
		if (_instance != null) return;
		_instance = this;
		// 1. 保存配置 (如果为空则抛出异常)
		_config = config ?? throw new ArgumentNullException(nameof(config));

		// 2. 立即校验 URL，发现错误直接报错，不要等到 Start 才报错
		if (string.IsNullOrEmpty(_config.Url))
		{
			throw new ArgumentException("配置中的 URL 不能为空");
		}

		// 自动补全 URL 结尾的斜杠
		if (!_config.Url.EndsWith("/"))
		{
			_config.Url += "/";
		}

		// 3. 预初始化 Sender
		// 如果配置里填了 BotQq，就用它；没填就用 0 (等连上会自动更新)
		long initialId = _config.BotQq ?? 0;
		Sender = new BotActionSender(_activeBots, Log, initialId);
	}

	/// <summary>
	/// 【修改】启动服务 (不再需要参数)
	/// </summary>
	public async Task Start()
	{
		_listener = new HttpListener();
		_listener.Prefixes.Add(_config.Url);
		try
		{
			_listener.Start();
			_cts = new CancellationTokenSource();

			Log($"[系统] 服务启动成功");
			Log($"[监听] {_config.Url}");

			if (!string.IsNullOrEmpty(_config.Token))
			{
				Log($"[安全] 鉴权模式已启用");
			}

			if (_config.BotQq != null && _config.BotQq != 0)
			{
				Log($"[配置] 绑定机器人 QQ: {_config.BotQq}");
			}

			_ = AcceptConnectionsLoop();
		}
		catch (HttpListenerException ex)
		{
			Log($"[启动失败] {ex.Message} (请检查端口占用或管理员权限)");
			throw;
		}

		StartFinish?.Invoke(this);
	}

	public void Stop()
	{
		_cts?.Cancel();
		_listener?.Stop();
		foreach (var socket in _activeBots.Values) socket.Dispose();
		_activeBots.Clear();
		Log("[系统] 服务已停止");
	}

	private async Task AcceptConnectionsLoop()
	{
		while (!_cts.IsCancellationRequested)
		{
			try
			{
				var context = await _listener.GetContextAsync();
				if (context.Request.IsWebSocketRequest) _ = HandleWebsocketHandshake(context);
				else
				{
					context.Response.StatusCode = 400;
					context.Response.Close();
				}
			}
			catch (HttpListenerException)
			{
			}
			catch (Exception ex)
			{
				Log($"[异常] {ex.Message}");
			}
		}
	}

	private async Task HandleWebsocketHandshake(HttpListenerContext context)
	{
		long botId = 0;
		WebSocket? socket = null;
		try
		{
			// 1. 鉴权
			if (!string.IsNullOrEmpty(_config.Token))
			{
				string? auth = context.Request.Headers["Authorization"];
				if (string.IsNullOrEmpty(auth) || auth != $"Bearer {_config.Token}")
				{
					Log($"[拒绝连接] Token 错误");
					context.Response.StatusCode = 401;
					context.Response.Close();
					return;
				}
			}

			// 2. 获取 QQ
			long.TryParse(context.Request.Headers["X-Self-ID"], out botId);

			// 3. 限制 QQ
			if (_config.BotQq != null && _config.BotQq != 0 && botId != _config.BotQq)
			{
				Log($"[拒绝连接] ID不匹配，配置要求: {_config.BotQq}，实际: {botId}");
				context.Response.StatusCode = 403;
				context.Response.Close();
				return;
			}

			// 4. 握手
			var wsContext = await context.AcceptWebSocketAsync(null);
			socket = wsContext.WebSocket;
			_activeBots[botId] = socket;

			// 【关键】连接成功后，刷新 Sender，确保 ID 是完全正确的
			Sender = new BotActionSender(_activeBots, Log, botId);

			Log($"[连接] 机器人 {botId} 已接入");
			BotOnline?.Invoke();
			await ReceiveMessageLoop(socket, botId);
		}
		catch (Exception ex)
		{
			Log($"[连接异常] {ex.Message}");
		}
		finally
		{
			if (socket != null)
			{
				_activeBots.TryRemove(botId, out _);
				socket.Dispose();
			}
		}
	}

	// ... ReceiveMessageLoop 和 ProcessReceivedJson 保持不变 ...
	private async Task ReceiveMessageLoop(WebSocket socket, long botId)
	{
		var buffer = new byte[1024 * 128];
		while (socket.State == WebSocketState.Open && !_cts.IsCancellationRequested)
		{
			var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
			if (result.MessageType == WebSocketMessageType.Close) break;
			if (result.MessageType == WebSocketMessageType.Text)
			{
				string json = Encoding.UTF8.GetString(buffer, 0, result.Count);
				ProcessReceivedJson(json, botId);
			}
		}
	}

	private void ProcessReceivedJson(string json, long botId)
	{
		try
		{
			var jsonObj = JObject.Parse(json);
			if (jsonObj["post_type"]?.ToString() == "meta_event") return;

			if (jsonObj["post_type"]?.ToString() == "message" && jsonObj["message_type"]?.ToString() == "group")
				OnGroupMessageReceived?.Invoke(jsonObj.ToObject<GroupMessageEvent>());
			else
				OnOtherEventReceived?.Invoke(jsonObj, botId);
		}
		catch
		{
		}
	}

	private void Log(string msg) => OnLog?.Invoke(msg);
}