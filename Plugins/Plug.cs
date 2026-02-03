using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using BkyhBot.BotConnect;
using BkyhBot.Class;

namespace BkyhBot.Plugins;

/// <summary>
/// 插件基类 - 所有的插件都要继承这个类哦！
/// <para>T: 插件的配置类 (必须继承自 PlugMessage)</para>
/// <para>TA: 插件本身的类 (就是继承 Plug 的那个类)</para>
/// </summary>
public abstract class Plug<T, TA>
	where T : PlugMessage, new()
	where TA : Plug<T, TA>, new()
{
	#region 核心属性

	/// <summary>
	/// 插件的配置信息 (对应配置文件里的内容)
	/// </summary>
	public T Message { get; protected set; }

	/// <summary>
	/// 默认配置信息
	/// <para>如果配置文件不存在，会使用这个对象生成默认文件</para>
	/// </summary>
	public T DefaultMessage { get; set; } = new T();

	/// <summary>
	/// 机器人的主连接实例
	/// <para>可以通过这个对象发送消息、获取群列表等</para>
	/// </summary>
	public BotConnect.BotConnect Bot { get; protected set; }

	/// <summary>
	/// 当前插件配置文件的完整路径
	/// </summary>
	public string ConfigPath { get; protected set; }

	#endregion

	#region 单例模式

	/// <summary>
	/// 保存当前插件的唯一实例
	/// </summary>
	protected static TA _instance;

	/// <summary>
	/// 获取插件的单例对象 (全局唯一)
	/// </summary>
	public static TA Instance => _instance;

	/// <summary>
	/// 构造函数受保护，防止在外部随意 new 出实例
	/// </summary>
	protected Plug()
	{
	}

	#endregion

	#region 启动流程

	/// <summary>
	/// 启动插件 (这是入口哦！)
	/// </summary>
	/// <param name="path">自定义配置文件路径 (留空则自动生成)</param>
	public static void Start(string path = null)
	{
		// 防止重复启动，保护哥哥的内存
		if (_instance != null)
		{
			Console.WriteLine($"[警告] 插件 {typeof(TA).Name} 已经启动过了，请勿重复启动。");
			return;
		}

		// 1. 创建实例
		_instance = new TA();

		// 2. 获取机器人连接实例 (如果还没连接，可能为 null，要注意哦)
		_instance.Bot = BotConnect.BotConnect.Instance;

		// 3. 加载配置
		_instance.LoadConfig(path);
		// 4. 执行子类的初始化逻辑 (如果有的话)
		_instance.OnInit();
		
		// 5. 打印启动成功消息
		string plugName = _instance.Message?.PlugName ?? "未知插件";
		Console.WriteLine($"[系统] <{plugName}> 启动完成！配置路径: {_instance.ConfigPath}");
	}

	/// <summary>
	/// [虚方法] 插件初始化钩子
	/// <para>哥哥可以在子类重写这个方法，用来做一些启动后的事情</para>
	/// <para>比如: 启动定时器、登录第三方网站、注册事件监听等</para>
	/// </summary>
	protected virtual void OnInit()
	{
		// 默认什么都不做，留给子类发挥~
	}

	#endregion

	#region 配置管理

	/// <summary>
	/// 加载配置文件
	/// </summary>
	/// <param name="path">文件路径</param>
	protected void LoadConfig(string path = null)
	{
		// 1. 确定路径
		if (string.IsNullOrEmpty(path))
		{
			// 如果 Bot 实例还没准备好，就用默认的 "Plugins" 文件夹
			string baseDir = Bot?.Config?.PlugConfigPath ?? "Plugins";
			// 拼接路径: Plugins/插件类名.json
			path = Path.Combine(baseDir, $"{typeof(TA).Name}.json");
		}

		ConfigPath = path;

		try
		{
			// 2. 确保文件夹存在 (贴心的自动创建)
			string dir = Path.GetDirectoryName(ConfigPath);
			if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
			{
				Directory.CreateDirectory(dir);
			}

			// 3. 读取或创建文件
			if (File.Exists(ConfigPath))
			{
				string json = File.ReadAllText(ConfigPath);
				// 这里加个 try-catch，万一 JSON 格式错了，不至于让整个程序崩溃
				try
				{
					Message = JsonSerializer.Deserialize<T>(json) ?? new T();
				}
				catch (JsonException)
				{
					Console.WriteLine($"[错误] <{typeof(TA).Name}> 配置文件格式错误，已加载空配置。请检查 JSON 格式！");
					Message = new T();
				}
			}
			else
			{
				// 文件不存在，创建默认配置
				Message = DefaultMessage ?? new T();
				SaveConfig(); // 保存默认配置到硬盘
				Console.WriteLine($"[系统] 已为 <{typeof(TA).Name}> 生成默认配置文件。");
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine($"[严重错误] 加载配置失败 <{typeof(TA).Name}>: {ex.Message}");
			Message = new T(); // 最后的保底，防止空引用
		}

		// 4. 注册到机器人的插件列表中 (前提是 Bot 和 Message 都不为空)
		if (Bot != null && Message != null)
		{
			Bot.PlugMessageList.Add(Message);
		}
	}

	/// <summary>
	/// 保存当前配置到文件
	/// </summary>
	public void SaveConfig()
	{
		try
		{
			var options = new JsonSerializerOptions
			{
				WriteIndented = true, // 格式化 JSON，让人类更容易看懂
				Encoder = JavaScriptEncoder.Create(UnicodeRanges.All) // 关键！防止中文变成 \uXXXX 乱码
			};

			string json = JsonSerializer.Serialize(Message, options);
			File.WriteAllText(ConfigPath, json);
			// Console.WriteLine($"[系统] <{Message?.PlugName}> 配置已保存。"); 
		}
		catch (Exception ex)
		{
			Console.WriteLine($"[错误] 保存配置失败 <{typeof(TA).Name}>: {ex.Message}");
		}
	}

	#endregion
}