using Newtonsoft.Json;

namespace BkyhBot.Class
{
	/// <summary>
	/// 消息类型
	/// </summary>
	/// <summary>
	/// 消息段类型枚举 - 用于定义各种消息内容格式
	/// </summary>
	public enum MsgType
	{
		/// <summary> 纯文本 </summary>
		Text,

		/// <summary> FaceQQ 表情 </summary>
		Face,

		/// <summary> 图片 (支持通过子类型区分 mface/image) </summary>
		Image,

		/// <summary> 语音 </summary>
		Record,

		/// <summary> 视频 </summary>
		Video,

		/// <summary> @某人 </summary>
		At,

		/// <summary> 猜拳魔法表情 </summary>
		Rps,

		/// <summary> 骰子 </summary>
		Dice,

		/// <summary> 私聊窗口抖动 (注意：部分场景可能受限) </summary>
		Shake,

		/// <summary> 群聊戳一戳 </summary>
		Poke,

		/// <summary> 链接分享 (JSON格式) - [当前状态：❌ 不通过消息发送] </summary>
		Share,

		/// <summary> 推荐好友/群 (JSON格式) </summary>
		Contact,

		/// <summary> 位置分享 (JSON格式) - [当前状态：❌ 不通过消息发送] </summary>
		Location,

		/// <summary> 音乐分享 (JSON格式) </summary>
		Music,

		/// <summary> 回复消息 </summary>
		Reply,

		/// <summary> 转发消息 </summary>
		Forward,

		/// <summary> 转发消息节点 </summary>
		Node,

		/// <summary> JSON 原生信息 </summary>
		Json,

		/// <summary> 表情包 (通常以上报为准) </summary>
		MFace,

		/// <summary> 文件发送 </summary>
		File,

		/// <summary> Markdown (注意：需在双层合并转发内使用) </summary>
		Markdown,

		/// <summary> 小程序卡片 (需调用扩展接口 get_mini_app_ark) </summary>
		LightApp
	}


	public abstract class PlugMessage
	{
		public bool PlugIsOpen { get; set; } = true; // 默认开启
		public string PlugName { get; set; } = "插件名称";
		public string Description { get; set; } = "插件描述";
	}
	/// <summary>
	/// 机器人配置类
	/// </summary>
	public class Config
	{
		/// <summary>
		/// 指定机器人的 QQ 号。
		/// 如果赋值，则只允许该 QQ 连接；如果不赋值（null），则允许任意 QQ 连接。
		/// </summary>
		public long? BotQq { get; set; }

		/// <summary>
		/// 监听地址 (HttpListener 格式)。
		/// 示例: "http://127.0.0.1:3001/" 或 "http://*:3001/"
		/// </summary>
		public string? Url { get; set; }

		/// <summary>
		/// 鉴权 Token。
		/// 如果赋值，NapCat 必须在配置中填写相同的 Token 才能连接。
		/// </summary>
		public string? Token { get; set; }
		public string? PlugConfigPath { get; set; }
		public long MasterQq { get; set; }
		public string Name { get; set; } = "夜语真白";
	}

	/// <summary>
	/// 群消息事件实体类
	/// </summary>
	public class GroupMessageEvent
	{
		/// <summary>
		/// 收到消息的机器人 QQ 号
		/// </summary>
		[JsonProperty("self_id")]
		public long SelfId { get; set; }

		/// <summary>
		/// 发送者的 QQ 号
		/// </summary>
		[JsonProperty("user_id")]
		public long UserId { get; set; }

		/// <summary>
		/// 消息发送时间 (Unix 时间戳)
		/// </summary>
		[JsonProperty("time")]
		public long Time { get; set; }

		/// <summary>
		/// 消息 ID (用于撤回等操作)
		/// </summary>
		[JsonProperty("message_id")]
		public int MessageId { get; set; }

		/// <summary>
		/// 消息序号
		/// </summary>
		[JsonProperty("message_seq")]
		public int MessageSeq { get; set; }

		/// <summary>
		/// 真实 ID
		/// </summary>
		[JsonProperty("real_id")]
		public int RealId { get; set; }

		/// <summary>
		/// 真实序号 (注意：JSON里这个字段是字符串类型)
		/// </summary>
		[JsonProperty("real_seq")]
		public string RealSeq { get; set; }

		/// <summary>
		/// 消息类型: group (群聊) 或 private (私聊)
		/// </summary>
		[JsonProperty("message_type")]
		public string MessageType { get; set; }

		/// <summary>
		/// 发送者详细信息
		/// </summary>
		[JsonProperty("sender")]
		public SenderInfo Sender { get; set; }

		/// <summary>
		/// 原始消息内容 (CQ码格式 string)
		/// </summary>
		[JsonProperty("raw_message")]
		public string RawMessage { get; set; }

		/// <summary>
		/// 字体 ID
		/// </summary>
		[JsonProperty("font")]
		public int Font { get; set; }

		/// <summary>
		/// 子类型 (normal, anonymous, notice 等)
		/// </summary>
		[JsonProperty("sub_type")]
		public string SubType { get; set; }

		/// <summary>
		/// 消息链 (数组格式的消息内容)
		/// </summary>
		[JsonProperty("message")]
		public List<MessageNode> Message { get; set; }

		/// <summary>
		/// 消息格式: array 或 string
		/// </summary>
		[JsonProperty("message_format")]
		public string MessageFormat { get; set; }

		/// <summary>
		/// 上报类型: message, request, notice, meta_event
		/// </summary>
		[JsonProperty("post_type")]
		public string PostType { get; set; }

		/// <summary>
		/// 群号
		/// </summary>
		[JsonProperty("group_id")]
		public long GroupId { get; set; }

		/// <summary>
		/// 群名称
		/// </summary>
		[JsonProperty("group_name")]
		public string GroupName { get; set; }
	}

	/// <summary>
	/// 发送者信息
	/// </summary>
	public class SenderInfo
	{
		[JsonProperty("user_id")] public long UserId { get; set; }

		[JsonProperty("nickname")] public string Nickname { get; set; }

		[JsonProperty("card")] public string Card { get; set; } // 群名片/备注

		[JsonProperty("role")] public string Role { get; set; } // owner(群主), admin(管理员), member(成员)
	}

	/// <summary>
	/// 消息链节点 (对应 array 格式的每一个元素)
	/// </summary>
	public class MessageNode
	{
		/// <summary>
		/// 消息类型: text, at, face, image 等
		/// </summary>
		[JsonProperty("type")]
		public string Type { get; set; }

		/// <summary>
		/// 数据内容 (根据类型不同，字段也不同，所以用 Dictionary 比较通用)
		/// 例如: {"text": "你好"} 或 {"qq": "123456"}
		/// </summary>
		[JsonProperty("data")]
		public Dictionary<string, string> Data { get; set; }
	}
}