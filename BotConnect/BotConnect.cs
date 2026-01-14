using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Collections.Concurrent;
using BkyhBot.BotAction;
using BkyhBot.Class;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BkyhBot.BotConnect;

/// <summary>
/// 机器人连接管理类：负责监听 WebSocket 连接并分发消息
/// </summary>
public class BotConnect
{
	private HttpListener? _listener;

	private CancellationTokenSource? _cts;

	// 使用并发字典存储活跃的机器人连接，确保线程安全
	private readonly ConcurrentDictionary<long, WebSocket> _activeBots = new();

	// 只读配置，确保运行期间配置不被外部篡改
	private readonly Config _config;
	public Config Config => _config;

	// 插件信息列表
	public List<PlugMessage> PlugMessageList { get; set; } = new();

	// 操作发送器：用于向机器人发送动作（如发消息）
	public BotActionSender Sender { get; private set; }

	// ================== 事件定义 ==================
	public event Action<GroupMessageEvent>? OnGroupMessageReceived;
	public event Action<JObject, long>? OnOtherEventReceived;
	public event Action<string>? OnLog;
	public event Action? BotOnline;
	public event Action<BotConnect>? StartFinish;

	// 单例模式（按需保留，但建议通过依赖注入管理）
	private static BotConnect? _instance;
	public static BotConnect Instance => _instance!;

	/// <summary>
	/// 构造函数：初始化配置与校验
	/// </summary>
	public BotConnect(Config config)
	{
		_instance = this;
		_config = config ?? throw new ArgumentNullException(nameof(config));

		// 校验并格式化监听地址
		if (string.IsNullOrWhiteSpace(_config.Url))
			throw new ArgumentException("配置中的监听 URL 不能为空");

		if (!_config.Url.EndsWith("/"))
			_config.Url += "/";

		// 预初始化发送器，防止空引用
		long initialId = _config.BotQq ?? 0;
		Sender = new BotActionSender(_activeBots, Log, initialId);
	}

	/// <summary>
	/// 启动异步监听服务
	/// </summary>
	public async Task Start()
	{
		if (_listener?.IsListening == true) return;

		_listener = new HttpListener();
		_listener.Prefixes.Add(_config.Url);

		try
		{
			_listener.Start();
			_cts = new CancellationTokenSource();

			Log($"[系统] 服务启动成功 | 监听: {_config.Url}");

			if (!string.IsNullOrEmpty(_config.Token)) Log("[安全] 鉴权模式已启用");
			if (_config.BotQq > 0) Log($"[配置] 绑定机器人 QQ: {_config.BotQq}");

			// 开启不阻塞的监听循环
			_ = AcceptConnectionsLoop(_cts.Token);

			StartFinish?.Invoke(this);
		}
		catch (HttpListenerException ex)
		{
			Log($"[启动失败] 端口被占用或无权限: {ex.Message}");
			throw;
		}
	}

	/// <summary>
	/// 停止服务并释放所有连接
	/// </summary>
	public void Stop()
	{
		_cts?.Cancel();
		_listener?.Stop();

		foreach (var socket in _activeBots.Values)
		{
			// 优雅关闭 WebSocket
			socket.Abort();
			socket.Dispose();
		}

		_activeBots.Clear();
		Log("[系统] 服务已停止");
	}

	/// <summary>
	/// 循环接收新的 HTTP/WebSocket 请求
	/// </summary>
	private async Task AcceptConnectionsLoop(CancellationToken ct)
	{
		while (!ct.IsCancellationRequested && _listener != null)
		{
			try
			{
				// 异步等待新的请求
				var context = await _listener.GetContextAsync();

				if (context.Request.IsWebSocketRequest)
				{
					// 使用 _ 丢弃 Task，实现非阻塞处理多客户端
					_ = HandleWebsocketHandshake(context, ct);
				}
				else
				{
					context.Response.StatusCode = 400;
					context.Response.Close();
				}
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				if (_listener?.IsListening == true)
					Log($"[监听异常] {ex.Message}");
			}
		}
	}

	/// <summary>
	/// 处理 WebSocket 握手、鉴权及生命周期
	/// </summary>
	private async Task HandleWebsocketHandshake(HttpListenerContext context, CancellationToken ct)
	{
		long botId = 0;
		WebSocket? socket = null;
		try
		{
			// 1. Token 鉴权校验
			if (!string.IsNullOrEmpty(_config.Token))
			{
				string? auth = context.Request.Headers["Authorization"];
				if (string.IsNullOrEmpty(auth) || auth != $"Bearer {_config.Token}")
				{
					Log("[拒绝连接] Token 错误或未提供");
					context.Response.StatusCode = 401;
					context.Response.Close();
					return;
				}
			}

			// 2. 解析机器人 QQ 号 (从消息头获取)
			long.TryParse(context.Request.Headers["X-Self-ID"], out botId);

			// 3. 校验 QQ 号是否匹配配置
			if (_config.BotQq.HasValue && _config.BotQq != 0 && botId != _config.BotQq)
			{
				Log($"[拒绝连接] ID不匹配 (期望:{_config.BotQq}, 实际:{botId})");
				context.Response.StatusCode = 403;
				context.Response.Close();
				return;
			}

			// 4. 完成握手并记录连接
			var wsContext = await context.AcceptWebSocketAsync(null);
			socket = wsContext.WebSocket;
			_activeBots[botId] = socket;

			// 动态更新发送器（确保多机器人环境下 ID 正确）
			Sender = new BotActionSender(_activeBots, Log, botId);

			Log($"[连接] 机器人 {botId} 已成功接入");
			BotOnline?.Invoke();

			// 5. 进入消息接收死循环，直到断开连接
			await ReceiveMessageLoop(socket, botId, ct);
		}
		catch (Exception ex)
		{
			Log($"[连接异常] Bot:{botId} - {ex.Message}");
		}
		finally
		{
			// 连接断开时的清理工作
			if (socket != null)
			{
				_activeBots.TryRemove(botId, out _);
				socket.Dispose();
				Log($"[断开] 机器人 {botId} 连接已关闭");
			}
		}
	}

	/// <summary>
	/// 核心接收循环：支持处理超大数据包，防止程序卡死
	/// </summary>
	private async Task ReceiveMessageLoop(WebSocket socket, long botId, CancellationToken ct)
	{
		// 128KB 缓冲区
		var buffer = new byte[1024 * 128];

		while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
		{
			using var ms = new MemoryStream();
			WebSocketReceiveResult result;

			try
			{
				// 循环读取直到收到完整的消息结束帧（EndOfMessage）
				// 这样即使消息超过 128KB 也不会报错或卡死
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

					// 建议使用异步或 Task.Run 处理业务逻辑，防止阻塞接收循环
					_ = Task.Run(() => ProcessReceivedJson(json, botId), ct);
				}
			}
			catch (Exception ex)
			{
				Log($"[读取消息异常] Bot:{botId} - {ex.Message}");
				break;
			}
		}
	}

	/// <summary>
	/// 解析收到的 JSON 消息并分发事件
	/// </summary>
	private void ProcessReceivedJson(string json, long botId)
	{
		if (string.IsNullOrWhiteSpace(json)) return;

		try
		{
			var jsonObj = JObject.Parse(json);

			// 过滤心跳等元事件，减少不必要的计算
			string? postType = jsonObj["post_type"]?.ToString();
			if (postType == "meta_event" || postType == null) return;

			if (postType == "message" && jsonObj["message_type"]?.ToString() == "group")
			{
				var groupEvent = jsonObj.ToObject<GroupMessageEvent>();
				if (groupEvent != null) OnGroupMessageReceived?.Invoke(groupEvent);
			}
			else
			{
				OnOtherEventReceived?.Invoke(jsonObj, botId);
			}
		}
		catch (JsonException ex)
		{
			// 仅记录解析失败，不抛出异常以防程序崩溃
			Log($"[JSON解析错误] {ex.Message}");
		}
		catch (Exception ex)
		{
			Log($"[逻辑处理错误] {ex.Message}");
		}
	}

	private void Log(string msg) => OnLog?.Invoke(msg);
}