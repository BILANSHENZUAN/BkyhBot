using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using BkyhBot.Class;

namespace BkyhBot.Plugins;

using BkyhBot.BotConnect;

public abstract class Plug<T> where T : PlugMessage, new()
{
	// 这里对应你在插件里调用的 Message (其实是 Config 配置对象)
	// 为了配合你的习惯，我把它命名为 Message
	public T Message { get; protected set; }
	protected T DefaultMessage; // 默认配置
	// 保存机器人实例
	public BotConnect Bot { get; protected set; }

	// 保存配置文件路径
	public string ConfigPath { get; protected set; }

	// 抽象的 Start 方法
	public abstract void Start();

	// 加载配置的具体实现
	protected void LoadConfig(string path)
	{
		try
		{
			// 确保目录存在
			string dir = Path.GetDirectoryName(path);
			if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
			{
				Directory.CreateDirectory(dir);
			}

			if (File.Exists(path))
			{
				string json = File.ReadAllText(path);
				Message = JsonSerializer.Deserialize<T>(json) ?? new T();
			}
			else
			{
				// 文件不存在则创建默认
				Message = DefaultMessage;
				var options = new JsonSerializerOptions { WriteIndented = true ,Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)};
				File.WriteAllText(path, JsonSerializer.Serialize(Message, options));
				Console.WriteLine($"[系统] 生成默认配置文件: {path}");
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine($"[错误] 加载配置失败: {ex.Message}");
			Message = new T(); // 失败时的保底
		}
		if (Bot != null)
		{
			Bot.PlugMessageList.Add(Message);
		}
	}
	protected void SaveConfig()
	{
		string json = JsonSerializer.Serialize(Message, new JsonSerializerOptions { WriteIndented = true });
		File.WriteAllText(ConfigPath, json);
	}
  }