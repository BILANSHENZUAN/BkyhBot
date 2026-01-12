# BkyhBot (BKYH机器人框架)

[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![OneBot](https://img.shields.io/badge/OneBot-v11-green.svg)](https://11.onebot.dev/)

**BkyhBot** 是一个专为 **NapCat/OneBot 11** 设计的轻量级 C# 机器人驱动框架。它采用插件化架构，支持高度自定义的消息处理和动作执行。

---

## 🛠️ 架构特色

### 1. 规范化的插件配置系统
项目采用了**“配置继承”**的设计模式。所有的插件配置类都继承自统一的基础信息类，确保了插件管理的标准化。

- **`PlugMessage` (基类)**：定义插件的开关、名称、功能描述等元数据。
- **`CustomConfig` (子类)**：继承基类并扩展插件特有的业务参数（如群号、黑名单、API Key等）。

### 2. 自动化生命周期管理
- **自动加载**：基类 `Plug<T>` 自动根据类名在 `Plug/` 目录下寻找对应的 JSON 配置文件。
- **自动初始化**：如果配置文件不存在，框架会根据配置类的默认值自动生成，实现“零配置”上手。

### 3. 灵活的消息驱动
- **强类型事件**：将 OneBot 的原始 JSON 转换为 C# 强类型对象，支持 LINQ 操作。
- **多媒体支持**：内置 CQ 码封装，支持图片、语音、闪照以及图文混合消息。

---

## 💻 代码示例：插件开发

得益于配置继承，开发一个新插件非常简单：

### 第一步：定义配置 (继承模式)
```csharp
// 插件特有的配置信息
public class MyActionConfig : PlugMessage // 继承插件基本信息类
{
    public long[] TargetGroups { get; set; } = []; // 业务特有参数
    public string ReplyText { get; set; } = "Hello World!";
}

```

### 第二步：编写业务逻辑

```csharp
public class MyAction : Plug<MyActionConfig>
{
    public MyAction(string configPath, BotConnect bot)
    {
        ConfigPath = configPath;
        Bot = bot;
    }

    public override void Start()
    {
        LoadConfig(ConfigPath);
        
        // 使用继承自基类的开关
        if (!Message.PlugIsOpen) return;

        Bot.OnGroupMessageReceived += async (e) => {
            if (Message.TargetGroups.Contains(e.GroupId)) {
                await Bot.Sender.SendGroupMessage(e.GroupId, Message.ReplyText);
            }
        };
    }
}

```

---

## 🚀 快速开始

1. **配置 NapCat**：开启反向 WebSocket，连接地址指向 `ws://你的服务器IP:端口/`。
2. **初始化项目**：
```csharp
var config = new Config { Url = "http://*:3001/", BotQq = 123456789 };
var bot = new BotConnect(config);

// 实例化并启动插件
new MyAction("Plug/MyAction.json", bot).Start();

await bot.Start();

```



---

## 📂 项目结构

* `BotConnect/`: 核心连接管理，负责 WebSocket 握手与消息分发。
* `BotAction/`: 动作执行器，封装了所有 OneBot API。
* `Plugins/`: 插件基类与接口定义。
* `Class/`: 实体模型与配置信息类。

---