using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using BkyhBot.Class;
using Newtonsoft.Json;

namespace BkyhBot.BotAction;

/// <summary>
/// 动作发送器 (绑定了具体机器人)
/// </summary>
public class BotActionSender
{
	// 引用连接池
	private readonly ConcurrentDictionary<long, WebSocket> _activeBots;

	// 日志委托
	private readonly Action<string> _logger;

	// 【新增】当前绑定的机器人 QQ 号
	private readonly long _selfId;
	public long SelfId => _selfId;
	/// <summary>
	/// 实例化发送器 (绑定具体的 QQ)
	/// </summary>
	/// <param name="activeBots">连接池引用</param>
	/// <param name="logger">日志方法</param>
	/// <param name="botId">要操作的机器人 QQ 号</param>
	public BotActionSender(ConcurrentDictionary<long, WebSocket> activeBots, Action<string> logger, long botId)
	{
		_activeBots = activeBots;
		_logger = logger;
		_selfId = botId; // 记住这个 ID
	}

	/// <summary>
	/// [底层方法] 发送 API 请求
	/// </summary>
	public async Task SendAction(string action, object parameters)
	{
		// 直接使用内部存储的 _selfId，不需要外部传入了
		if (_activeBots.TryGetValue(_selfId, out var socket) && socket.State == WebSocketState.Open)
		{
			var payload = new
			{
				action,
				@params = parameters,
				echo = Guid.NewGuid().ToString()
			};

			string json = JsonConvert.SerializeObject(payload);
			byte[] bytes = Encoding.UTF8.GetBytes(json);

			try
			{
				await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
			}
			catch (Exception ex)
			{
				_logger($"[发送异常] 机器人 {_selfId} 发送失败: {ex.Message}");
			}
		}
		else
		{
			_logger($"[发送失败] 机器人 {_selfId} 未连接或已断开。");
		}
	}

	// ================== 快捷方法 (移除了 botId 参数) ==================

	/// <summary>
	/// 发送群消息
	/// </summary>
	public async Task SendGroupMessage(long groupId, string message, bool autoEscape = false)
	{
		var parameters = new { group_id = groupId, message, auto_escape = autoEscape };
		await SendAction("send_group_msg", parameters);
	}

	/// <summary>
	/// 发送私聊消息
	/// </summary>
	public async Task SendPrivateMessage(long userId, string message, bool autoEscape = false)
	{
		var parameters = new { user_id = userId, message, auto_escape = autoEscape };
		await SendAction("send_private_msg", parameters);
	}

	/// <summary>
	/// 撤回消息
	/// </summary>
	public async Task DeleteMessage(int messageId)
	{
		await SendAction("delete_msg", new { message_id = messageId });
	}

	/// <summary>
	/// 发送群图片
	/// </summary>
	public async Task SendGroupImage(long groupId, string file)
	{
		string cqCode = $"[CQ:image,file={file}]";
		await SendGroupMessage(groupId, cqCode);
	}

	/// <summary>
	/// 发送私聊图片
	/// </summary>
	public async Task SendPrivateImage(long userId, string file)
	{
		string cqCode = $"[CQ:image,file={file}]";
		await SendPrivateMessage(userId, cqCode);
	}

	/// <summary>
	/// 发送群聊文本图片混合消息
	/// </summary>
	public async Task SendGroupImage(long groupId, string prefixText, string imageUrl, string suffixText = "")
	{
		string imgCode = $"[CQ:image,file={imageUrl}]";
		string msg = $"{prefixText}\n{imgCode}\n{suffixText}".Trim();
		await SendGroupMessage(groupId, msg);
	}

	/// <summary>
	/// 发送私聊文本图片混合消息
	/// </summary>
	public async Task SendPrivateImage(long userId, string prefixText, string imageUrl, string suffixText = "")
	{
		string imgCode = $"[CQ:image,file={imageUrl}]";
		string msg = $"{prefixText}\n{imgCode}\n{suffixText}".Trim();
		await SendPrivateMessage(userId, msg);
	}
	public async Task SendGroupMediaMessage(MsgType type, long groupId, string file)
	{
		string msg = $"[CQ:{type.ToString().ToLower()},file={file}]";
		await SendGroupMessage(groupId, msg);
	}
	public async Task SendPrivateMediaMessage(MsgType type, long userId, string file)
	{
		string msg = $"[CQ:{type.ToString().ToLower()},file={file}]";
		await SendPrivateMessage(userId, msg);
	}
}