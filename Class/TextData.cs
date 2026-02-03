using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BkyhBot.Class
{
	// ... (MsgType 枚举保持不变) ...
	public enum MsgType
	{
		Text,
		Face,
		Image,
		Record,
		Video,
		At,
		Rps,
		Dice,
		Shake,
		Poke,
		Share,
		Contact,
		Location,
		Music,
		Reply,
		Forward,
		Node,
		Json,
		MFace,
		File,
		Markdown,
		LightApp
	}

	public abstract class PlugMessage
	{
		public bool PlugIsOpen { get; set; } = true;
		public string PlugName { get; set; } = "插件名称";
		public string Description { get; set; } = "插件描述";
	}

	/// <summary>
	/// 机器人配置类
	/// </summary>
	public class Config
	{
		public string? BotQq { get; set; }
		public string? Url { get; set; }
		public string? Token { get; set; }
		public string MasterQq { get; set; }
		public string Name { get; set; } = "夜语真白";
		public string PlugConfigPath { get; set; } = "";

		// ================== [新增] 网页控制端配置 ==================

		/// <summary>
		/// 网页后台监听地址 (建议使用 http://*:5000 允许局域网访问)
		/// </summary>
		public string WebDashboardUrl { get; set; } = "http://*:5000";

		/// <summary>
		/// 网页后台管理密钥 (登录网页时需要输入)
		/// </summary>
		public string WebAdminSecret { get; set; } = "123456";

		// =========================================================

		/// <summary>
		/// 是否开启全群响应
		/// </summary>
		public bool EnableAllGroups { get; set; } = false;

		public List<string> GroupWhiteList { get; set; } = new();
		public List<string> GroupBlackList { get; set; } = new();
		public List<string> PrivateWhiteList { get; set; } = new();
		public List<string> PrivateBlackList { get; set; } = new();
	}
	// ... (BaseEvent, MessageEvent 等其他类保持不变，为了节省篇幅就不重复粘贴啦，只替换 Config 类即可) ...
	// 下面为了完整性，把关键的 Event 类也列出来，你可以直接复制替换整个文件

	public class BaseEvent
	{
		[JsonProperty("self_id")] public string SelfId { get; set; }
		[JsonProperty("time")] public string Time { get; set; }
		[JsonProperty("post_type")] public string PostType { get; set; }
	}

	public class MessageEvent : BaseEvent
	{
		[JsonProperty("message_type")] public string MessageType { get; set; }
		[JsonProperty("sub_type")] public string SubType { get; set; }
		[JsonProperty("message_id")] public int MessageId { get; set; }
		[JsonProperty("user_id")] public string UserId { get; set; }
		[JsonProperty("message")] public List<MessageNode> Message { get; set; }
		[JsonProperty("raw_message")] public string RawMessage { get; set; }
		[JsonProperty("font")] public int Font { get; set; }
		[JsonProperty("sender")] public SenderInfo Sender { get; set; }
		[JsonProperty("message_seq")] public int MessageSeq { get; set; }
		[JsonProperty("real_id")] public int RealId { get; set; }
		[JsonProperty("real_seq")] public string RealSeq { get; set; }
		[JsonProperty("message_format")] public string MessageFormat { get; set; }
	}

	public class GroupMessageEvent : MessageEvent
	{
		[JsonProperty("group_id")] public string GroupId { get; set; }
		[JsonProperty("group_name")] public string GroupName { get; set; }
		[JsonProperty("anonymous")] public AnonymousInfo Anonymous { get; set; }
	}

	public class PrivateMessageEvent : MessageEvent
	{
		[JsonProperty("target_id")] public string TargetId { get; set; }
		[JsonProperty("temp_source")] public int TempSource { get; set; }
	}

	public class NoticeEvent : BaseEvent
	{
		[JsonProperty("notice_type")] public string NoticeType { get; set; }
		[JsonProperty("group_id")] public string GroupId { get; set; }
		[JsonProperty("user_id")] public string UserId { get; set; }
		[JsonProperty("sub_type")] public string SubType { get; set; }
		[JsonProperty("operator_id")] public string OperatorId { get; set; }
		[JsonProperty("target_id")] public string TargetId { get; set; }
		[JsonProperty("duration")] public long Duration { get; set; }
		[JsonProperty("file")] public FileInfo File { get; set; }
		[JsonProperty("honor_type")] public string HonorType { get; set; }
	}

	public class RequestEvent : BaseEvent
	{
		[JsonProperty("request_type")] public string RequestType { get; set; }
		[JsonProperty("user_id")] public string UserId { get; set; }
		[JsonProperty("group_id")] public string GroupId { get; set; }
		[JsonProperty("comment")] public string Comment { get; set; }
		[JsonProperty("flag")] public string Flag { get; set; }
		[JsonProperty("sub_type")] public string SubType { get; set; }
	}

	public class MetaEvent : BaseEvent
	{
		[JsonProperty("meta_event_type")] public string MetaEventType { get; set; }
		[JsonProperty("status")] public JObject Status { get; set; }
		[JsonProperty("interval")] public long Interval { get; set; }
	}

	public class SenderInfo
	{
		[JsonProperty("user_id")] public string UserId { get; set; }
		[JsonProperty("nickname")] public string Nickname { get; set; }
		[JsonProperty("card")] public string Card { get; set; }
		[JsonProperty("role")] public string Role { get; set; }
	}

	public class MessageNode
	{
		[JsonProperty("type")] public string Type { get; set; }
		[JsonProperty("data")] public Dictionary<string, string> Data { get; set; }
	}

	public class AnonymousInfo
	{
		[JsonProperty("id")] public long Id { get; set; }
		[JsonProperty("name")] public string Name { get; set; }
		[JsonProperty("flag")] public string Flag { get; set; }
	}

	public class FileInfo
	{
		[JsonProperty("id")] public string Id { get; set; }
		[JsonProperty("name")] public string Name { get; set; }
		[JsonProperty("size")] public long Size { get; set; }
		[JsonProperty("busid")] public long BusId { get; set; }
	}
}