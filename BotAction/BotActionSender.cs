using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using BkyhBot.Class;
using Newtonsoft.Json;

namespace BkyhBot.BotAction;

/// <summary>
/// 动作发送器 (绑定了具体机器人)
/// <para>负责将 API 请求通过 WebSocket 发送给 OneBot 客户端</para>
/// </summary>
public class BotActionSender
{
	private readonly ConcurrentDictionary<string, WebSocket> _activeBots;
	private readonly Action<string> _logger;
	private readonly string _selfId;

	/// <summary>
	/// 获取当前发送器绑定的机器人 QQ 号
	/// </summary>
	public string SelfId => _selfId;

	public BotActionSender(ConcurrentDictionary<string, WebSocket> activeBots, Action<string> logger, string botId)
	{
		_activeBots = activeBots;
		_logger = logger;
		_selfId = botId;
	}

	/// <summary>
	/// [底层核心] 发送 API 请求
	/// <para>修改：返回 echo (UUID string)，以便后续支持同步等待结果</para>
	/// </summary>
	public async Task<string> SendAction(string action, object parameters)
	{
		if (_activeBots.TryGetValue(_selfId, out var socket) && socket.State == WebSocketState.Open)
		{
			// 生成唯一请求ID
			string echo = Guid.NewGuid().ToString("N");

			var payload = new
			{
				action,
				@params = parameters,
				echo // 附带 echo
			};

			string json = JsonConvert.SerializeObject(payload);
			byte[] bytes = Encoding.UTF8.GetBytes(json);

			try
			{
				await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
				return echo;
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

		return string.Empty;
	}

	// ========================================================================
	//                         1. 基础消息发送 (保持完全兼容)
	// ========================================================================

	/// <summary>
	/// 发送群消息
	/// </summary>
	public async Task SendGroupMessage(string groupId, string message, bool autoEscape = false)
	{
		// 自动将参数转为匿名对象
		await SendAction("send_group_msg", new { group_id = groupId, message, auto_escape = autoEscape });
	}

	/// <summary>
	/// 发送私聊消息
	/// </summary>
	public async Task SendPrivateMessage(string userId, string message, bool autoEscape = false)
	{
		await SendAction("send_private_msg", new { user_id = userId, message, auto_escape = autoEscape });
	}

	/// <summary>
	/// 撤回消息
	/// </summary>
	public async Task DeleteMessage(int messageId)
	{
		await SendAction("delete_msg", new { message_id = messageId });
	}

	/// <summary>
	/// 发送群图片 (CQ码封装)
	/// </summary>
	public async Task SendGroupImage(string groupId, string file)
	{
		string cqCode = $"[CQ:image,file={file}]";
		await SendGroupMessage(groupId, cqCode);
	}

	/// <summary>
	/// 发送私聊图片 (CQ码封装)
	/// </summary>
	public async Task SendPrivateImage(string userId, string file)
	{
		string cqCode = $"[CQ:image,file={file}]";
		await SendPrivateMessage(userId, cqCode);
	}

	/// <summary>
	/// 发送群聊文本图片混合消息 (旧逻辑保留)
	/// </summary>
	public async Task SendGroupImage(string groupId, string prefixText, string imageUrl, string suffixText = "")
	{
		string imgCode = $"[CQ:image,file={imageUrl}]";
		string msg = $"{prefixText}\n{imgCode}\n{suffixText}".Trim();
		await SendGroupMessage(groupId, msg);
	}

	/// <summary>
	/// 发送私聊文本图片混合消息 (旧逻辑保留)
	/// </summary>
	public async Task SendPrivateImage(string userId, string prefixText, string imageUrl, string suffixText = "")
	{
		string imgCode = $"[CQ:image,file={imageUrl}]";
		string msg = $"{prefixText}\n{imgCode}\n{suffixText}".Trim();
		await SendPrivateMessage(userId, msg);
	}

	/// <summary>
	/// 发送群聊多媒体消息 (通用)
	/// </summary>
	public async Task SendGroupMediaMessage(MsgType type, string groupId, string file)
	{
		string msg = $"[CQ:{type.ToString().ToLower()},file={file}]";
		await SendGroupMessage(groupId, msg);
	}

	/// <summary>
	/// 发送私聊多媒体消息 (通用)
	/// </summary>
	public async Task SendPrivateMediaMessage(MsgType type, string userId, string file)
	{
		string msg = $"[CQ:{type.ToString().ToLower()},file={file}]";
		await SendPrivateMessage(userId, msg);
	}

	// ========================================================================
	//                         2. [新增] 群组管理 (Kick, Ban, Admin)
	// ========================================================================

	/// <summary>
	/// 群组踢人
	/// </summary>
	public async Task SetGroupKick(string groupId, string userId, bool rejectAddRequest = false)
		=> await SendAction("set_group_kick",
			new { group_id = groupId, user_id = userId, reject_add_request = rejectAddRequest });

	/// <summary>
	/// 群组单人禁言
	/// </summary>
	public async Task SetGroupBan(string groupId, string userId, int duration = 1800)
		=> await SendAction("set_group_ban", new { group_id = groupId, user_id = userId, duration }); // duration=0 为解除

	/// <summary>
	/// 群组全员禁言
	/// </summary>
	public async Task SetGroupWholeBan(string groupId, bool enable = true)
		=> await SendAction("set_group_whole_ban", new { group_id = groupId, enable });

	/// <summary>
	/// 设置群管理员
	/// </summary>
	public async Task SetGroupAdmin(string groupId, string userId, bool enable = true)
		=> await SendAction("set_group_admin", new { group_id = groupId, user_id = userId, enable });

	/// <summary>
	/// 设置群名片 (空字符串为删除名片)
	/// </summary>
	public async Task SetGroupCard(string groupId, string userId, string card = "")
		=> await SendAction("set_group_card", new { group_id = groupId, user_id = userId, card });

	/// <summary>
	/// 设置群名
	/// </summary>
	public async Task SetGroupName(string groupId, string groupName)
		=> await SendAction("set_group_name", new { group_id = groupId, group_name = groupName });

	/// <summary>
	/// 退出群组
	/// </summary>
	public async Task LeaveGroup(string groupId, bool isDismiss = false)
		=> await SendAction("set_group_leave", new { group_id = groupId, is_dismiss = isDismiss });

	/// <summary>
	/// 设置群头衔 (需群主权限)
	/// </summary>
	public async Task SetGroupSpecialTitle(string groupId, string userId, string specialTitle, int duration = -1)
		=> await SendAction("set_group_special_title",
			new { group_id = groupId, user_id = userId, special_title = specialTitle, duration });

	// ========================================================================
	//                         3. [新增] 请求处理 (Request)
	// ========================================================================

	/// <summary>
	/// 处理加好友请求
	/// </summary>
	public async Task SetFriendAddRequest(string flag, bool approve = true, string remark = "")
		=> await SendAction("set_friend_add_request", new { flag, approve, remark });

	/// <summary>
	/// 处理加群请求/邀请
	/// </summary>
	public async Task SetGroupAddRequest(string flag, string subType, bool approve = true, string reason = "")
		=> await SendAction("set_group_add_request", new { flag, sub_type = subType, approve, reason });

	// ========================================================================
	//                         4. [新增] 信息获取 (Get Info)
	// ========================================================================

	public async Task GetLoginInfo() => await SendAction("get_login_info", new { });

	public async Task GetStrangerInfo(string userId, bool noCache = false)
		=> await SendAction("get_stranger_info", new { user_id = userId, no_cache = noCache });

	public async Task GetFriendList() => await SendAction("get_friend_list", new { });

	public async Task GetGroupInfo(string groupId, bool noCache = false)
		=> await SendAction("get_group_info", new { group_id = groupId, no_cache = noCache });

	public async Task GetGroupList() => await SendAction("get_group_list", new { });

	public async Task GetGroupMemberInfo(string groupId, string userId, bool noCache = false)
		=> await SendAction("get_group_member_info", new { group_id = groupId, user_id = userId, no_cache = noCache });

	public async Task GetGroupMemberList(string groupId)
		=> await SendAction("get_group_member_list", new { group_id = groupId });

	// ========================================================================
	//                         5. [新增] 系统与资源
	// ========================================================================

	public async Task GetVersionInfo() => await SendAction("get_version_info", new { });

	public async Task GetStatus() => await SendAction("get_status", new { });

	public async Task CleanCache() => await SendAction("clean_cache", new { });

	public async Task<string> GetImage(string file) => await SendAction("get_image", new { file });
	// ========================================================================
	//                         6. [新增] 头像获取辅助 (Helper)
	// ========================================================================

	/// <summary>
	/// 获取用户头像链接 (高清)
	/// </summary>
	/// <param name="userId">用户QQ号</param>
	/// <returns>HTTP直链</returns>
	public string GetUserAvatarUrl(string userId)
	{
		// spec=640 是高清，100 是小图
		return $"https://q.qlogo.cn/headimg_dl?dst_uin={userId}&spec=640";
	}

	/// <summary>
	/// 获取群头像链接
	/// </summary>
	/// <param name="groupId">群号</param>
	/// <returns>HTTP直链</returns>
	public string GetGroupAvatarUrl(string groupId)
	{
		return $"https://p.qlogo.cn/gh/{groupId}/{groupId}/100";
	}
}