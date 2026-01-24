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

	#region 事件定义

	public event Action<GroupMessageEvent>? OnGroupMessageReceived;
	public event Action<JObject, string>? OnOtherEventReceived;
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
	/// [极简版] 直接使用相对路径加载配置
	/// <para>注意：调试时请将 VS 的工作目录设置为 $(ProjectDir)</para>
	/// </summary>
	private static Config LoadOrInitConfig()
	{
		// 1. 直接指定相对路径
		// 程序会在 "当前工作目录/Configs/Config.json" 寻找
		string configPath = Path.Combine("Configs", "Config.json");

		// 打印一下绝对路径，方便欧尼酱确认它到底在哪
		Console.WriteLine($"[系统] 配置文件路径: {Path.GetFullPath(configPath)}");

		// 2. 如果文件不存在，自动创建
		if (!File.Exists(configPath))
		{
			try
			{
				// 确保文件夹存在
				string dir = Path.GetDirectoryName(configPath)!;
				if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
					Directory.CreateDirectory(dir);

				var defaultConfig = new Config
				{
					Url = "http://127.0.0.1:3001/",
					BotQq = "123456789",
					MasterQq = "987654321",
					PlugConfigPath = "Plugins/"
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

		// 3. 读取配置
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
		try
		{
			var jsonObj = JObject.Parse(json);
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
		catch (Exception ex)
		{
			Log($"[数据处理异常] {ex.Message}");
		}
	}

	private void Log(string msg) => OnLog?.Invoke(msg);
}