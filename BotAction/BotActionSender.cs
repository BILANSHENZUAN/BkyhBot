using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using BkyhBot.Class;
using Newtonsoft.Json;

namespace BkyhBot.BotAction;

public class BotActionSender
{
	private readonly ConcurrentDictionary<string, WebSocket> _activeBots;
	private readonly Action<string> _logger;
	private readonly string _selfId;

	// [核心修复] 信号量锁：防止并发调用 SendAsync 导致 WebSocket 状态异常
	private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

	public string SelfId => _selfId;

	public BotActionSender(ConcurrentDictionary<string, WebSocket> activeBots, Action<string> logger, string botId)
	{
		_activeBots = activeBots;
		_logger = logger;
		_selfId = botId;
	}

	/// <summary>
	/// 核心发送方法 (线程安全版)
	/// </summary>
	public async Task<string> SendAction(string action, object parameters)
	{
		if (_activeBots.TryGetValue(_selfId, out var socket) && socket.State == WebSocketState.Open)
		{
			string echo = Guid.NewGuid().ToString("N");
			var payload = new { action, @params = parameters, echo };
			string json = JsonConvert.SerializeObject(payload);
			byte[] bytes = Encoding.UTF8.GetBytes(json);

			// 进入临界区，确保同一时间只有一个线程在发送
			await _sendLock.WaitAsync();
			try
			{
				await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
				return echo;
			}
			catch (Exception ex)
			{
				_logger($"[发送异常] 机器人 {_selfId} 发送失败: {ex.Message}");
			}
			finally
			{
				_sendLock.Release(); // 务必释放锁
			}
		}
		else
		{
			_logger($"[发送失败] 机器人 {_selfId} 未连接或已断开。");
		}

		return string.Empty;
	}

	// =================================================================
	//                 基础消息发送
	// =================================================================

	/// <summary>
	/// 发送群消息
	/// </summary>
	public async Task SendGroupMessage(string groupId, string message, bool autoEscape = false)
		=> await SendAction("send_group_msg", new { group_id = groupId, message, auto_escape = autoEscape });

	/// <summary>
	/// 发送好友私聊 (保持原样，兼容旧插件)
	/// </summary>
	public async Task SendPrivateMessage(string userId, string message, bool autoEscape = false)
		=> await SendAction("send_private_msg", new { user_id = userId, message, auto_escape = autoEscape });

	/// <summary>
	/// [新增] 发送群临时会话
	/// <para>专门用于非好友的私聊，必须指定来源群号</para>
	/// </summary>
	/// <param name="userId">对方QQ</param>
	/// <param name="groupId">来源群号</param>
	/// <param name="message">消息内容</param>
	public async Task SendTempMessage(string userId, string groupId, string message, bool autoEscape = false)
	{
		var paramsObj = new Dictionary<string, object>
		{
			{ "user_id", userId },
			{ "group_id", groupId }, // 显式指定群号，走临时会话通道
			{ "message", message },
			{ "auto_escape", autoEscape }
		};
		await SendAction("send_private_msg", paramsObj);
	}

	/// <summary>
	/// 撤回消息
	/// </summary>
	public async Task DeleteMessage(int messageId)
		=> await SendAction("delete_msg", new { message_id = messageId });

	// =================================================================
	//                 媒体消息发送
	// =================================================================

	public async Task SendGroupImage(string groupId, string file)
	{
		string cqCode = $"[CQ:image,file={file}]";
		await SendGroupMessage(groupId, cqCode);
	}

	public async Task SendPrivateImage(string userId, string file)
	{
		string cqCode = $"[CQ:image,file={file}]";
		await SendPrivateMessage(userId, cqCode);
	}

	/// <summary>
	/// 发送带文字的群图片
	/// </summary>
	public async Task SendGroupImage(string groupId, string prefixText, string imageUrl, string suffixText = "")
	{
		string imgCode = $"[CQ:image,file={imageUrl}]";
		string msg = $"{prefixText}\n{imgCode}\n{suffixText}".Trim();
		await SendGroupMessage(groupId, msg);
	}

	/// <summary>
	/// 发送带文字的私聊图片
	/// </summary>
	public async Task SendPrivateImage(string userId, string prefixText, string imageUrl, string suffixText = "")
	{
		string imgCode = $"[CQ:image,file={imageUrl}]";
		string msg = $"{prefixText}\n{imgCode}\n{suffixText}".Trim();
		await SendPrivateMessage(userId, msg);
	}

	public async Task SendGroupMediaMessage(MsgType type, string groupId, string file)
	{
		string msg = $"[CQ:{type.ToString().ToLower()},file={file}]";
		await SendGroupMessage(groupId, msg);
	}

	public async Task SendPrivateMediaMessage(MsgType type, string userId, string file)
	{
		string msg = $"[CQ:{type.ToString().ToLower()},file={file}]";
		await SendPrivateMessage(userId, msg);
	}

	// =================================================================
	//                 群管理操作
	// =================================================================

	/// <summary>
	/// 群组踢人
	/// </summary>
	public async Task SetGroupKick(string groupId, string userId, bool rejectAddRequest = false)
		=> await SendAction("set_group_kick",
			new { group_id = groupId, user_id = userId, reject_add_request = rejectAddRequest });

	/// <summary>
	/// 群组禁言
	/// </summary>
	public async Task SetGroupBan(string groupId, string userId, int duration = 1800)
		=> await SendAction("set_group_ban", new { group_id = groupId, user_id = userId, duration });

	/// <summary>
	/// 全员禁言
	/// </summary>
	public async Task SetGroupWholeBan(string groupId, bool enable = true)
		=> await SendAction("set_group_whole_ban", new { group_id = groupId, enable });

	/// <summary>
	/// 设置管理员
	/// </summary>
	public async Task SetGroupAdmin(string groupId, string userId, bool enable = true)
		=> await SendAction("set_group_admin", new { group_id = groupId, user_id = userId, enable });

	/// <summary>
	/// 设置群名片
	/// </summary>
	public async Task SetGroupCard(string groupId, string userId, string card = "")
		=> await SendAction("set_group_card", new { group_id = groupId, user_id = userId, card });

	/// <summary>
	/// 设置群名
	/// </summary>
	public async Task SetGroupName(string groupId, string groupName)
		=> await SendAction("set_group_name", new { group_id = groupId, group_name = groupName });

	/// <summary>
	/// 退出群聊
	/// </summary>
	public async Task LeaveGroup(string groupId, bool isDismiss = false)
		=> await SendAction("set_group_leave", new { group_id = groupId, is_dismiss = isDismiss });

	/// <summary>
	/// 设置专属头衔
	/// </summary>
	public async Task SetGroupSpecialTitle(string groupId, string userId, string specialTitle, int duration = -1)
		=> await SendAction("set_group_special_title",
			new { group_id = groupId, user_id = userId, special_title = specialTitle, duration });

	// =================================================================
	//                 请求处理 (加群/加好友)
	// =================================================================

	/// <summary>
	/// 处理加好友请求
	/// </summary>
	public async Task SetFriendAddRequest(string flag, bool approve = true, string remark = "")
		=> await SendAction("set_friend_add_request", new { flag, approve, remark });

	/// <summary>
	/// 处理加群请求
	/// </summary>
	public async Task SetGroupAddRequest(string flag, string subType, bool approve = true, string reason = "")
		=> await SendAction("set_group_add_request", new { flag, sub_type = subType, approve, reason });

	// =================================================================
	//                 信息获取
	// =================================================================

	public async Task GetLoginInfo() => await SendAction("get_login_info", new { });

	public async Task GetStrangerInfo(string userId, bool noCache = false) =>
		await SendAction("get_stranger_info", new { user_id = userId, no_cache = noCache });

	public async Task GetFriendList() => await SendAction("get_friend_list", new { });

	public async Task GetGroupInfo(string groupId, bool noCache = false) =>
		await SendAction("get_group_info", new { group_id = groupId, no_cache = noCache });

	public async Task GetGroupList() => await SendAction("get_group_list", new { });

	public async Task GetGroupMemberInfo(string groupId, string userId, bool noCache = false) =>
		await SendAction("get_group_member_info", new { group_id = groupId, user_id = userId, no_cache = noCache });

	public async Task GetGroupMemberList(string groupId) =>
		await SendAction("get_group_member_list", new { group_id = groupId });

	public async Task GetVersionInfo() => await SendAction("get_version_info", new { });
	public async Task GetStatus() => await SendAction("get_status", new { });
	public async Task CleanCache() => await SendAction("clean_cache", new { });
	public async Task<string> GetImage(string file) => await SendAction("get_image", new { file });

	// =================================================================
	//                 工具方法
	// =================================================================

	public string GetUserAvatarUrl(string userId) => $"https://q.qlogo.cn/headimg_dl?dst_uin={userId}&spec=640";
	public string GetGroupAvatarUrl(string groupId) => $"https://p.qlogo.cn/gh/{groupId}/{groupId}/100";
}