### README.md

```markdown
# BkyhBot (BKYH机器人框架)

[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0%2B-purple.svg)](https://dotnet.microsoft.com/)
[![OneBot](https://img.shields.io/badge/OneBot-v11-green.svg)](https://11.onebot.dev/)

**BkyhBot** 是一个基于 **C#** 编写的轻量级 QQ 机器人开发框架。它专为对接 **NapCat** (基于 NTQQ 的 OneBot 11 实现) 而设计，采用 **反向 WebSocket** 通信模式。

与常见的 ASP.NET 框架不同，BkyhBot 摒弃了沉重的 Web 依赖，使用原生的 `HttpListener` 实现高效、低占用的一键启动。

---

## ✨ 核心特性

- **🚀 轻量级核心**：不依赖 ASP.NET Core，极低的内存占用，适合在低配服务器或树莓派上运行。
- **🔌 插件系统**：内置泛型插件基类 `Plug<T>`，支持**配置文件自动生成**、**自动加载**和**热插拔**（逻辑上）。
- **⚡ 反向 WebSocket**：支持 Token 鉴权、多账号隔离（指定 QQ 号连接）。
- **🛠️ 开发者友好**：高度封装的 `BotActionSender`，让发送群消息、图片、混合消息变得像说话一样简单。
- **🎨 绘图支持**：(可选) 集成 SkiaSharp，支持将文字动态渲染为精美图片卡片。

---

## 📦 快速开始

### 1. 环境准备

- [NapCatQQ](https://github.com/NapNeko/NapCatQQ) (或其他 OneBot 11 实现)
- .NET 8.0 SDK 或更高版本

### 2. 配置 NapCat

在 NapCat 的 WebUI 或配置文件中，启用 **反向 WebSocket** 并设置地址：

- **URL**: `ws://127.0.0.1:3001/` (注意端口要与 BkyhBot 一致)
- **Token**: (可选，建议配置)

### 3. 创建你的第一个机器人

```csharp
using BkyhBot.BotConnect;
using BkyhBot.Class;

// 1. 配置连接信息
var config = new Config 
{
    Url = "http://*:3001/",   // 监听地址
    BotQq = 123456789,        // (可选) 仅允许指定 QQ 连接
    Token = "your_token"      // (可选) 鉴权 Token
};

// 2. 初始化框架
var bot = new BotConnect(config);

// 3. 注册简单的日志事件
bot.OnLog += Console.WriteLine;

// 4. 加载插件 (示例)
var myPlugin = new MyPlugin("Plug/MyPlugin.json", bot);
myPlugin.Start();

// 5. 启动服务
await bot.Start();

// 保持运行
await Task.Delay(-1);

```

---

## 🧩 插件开发指南

BkyhBot 拥有优雅的插件开发体验。你只需要继承 `Plug<T>`，框架会自动帮你处理配置文件的读写。

### 第一步：定义配置类

```csharp
public class EchoConfig
{
    public PlugMessage Message { get; set; } = new PlugMessage();
    public long[] GroupIds { get; set; } = Array.Empty<long>(); // 开启的群号
}

```

### 第二步：编写插件逻辑

```csharp
using BkyhBot.Plugins;

public class EchoPlugin : Plug<EchoConfig>
{
    // 构造函数：接收路径和 Bot 实例
    public EchoPlugin(string configPath, BotConnect bot)
    {
        ConfigPath = configPath;
        Bot = bot;
    }

    public override void Start()
    {
        // 1. 自动加载配置 (如果文件不存在会自动创建)
        LoadConfig(ConfigPath);
        
        // 2. 检查插件开关
        if (!Message.Message.PlugIsOpen) return;

        // 3. 注册消息事件
        Bot.OnGroupMessageReceived += OnGroupMessage;
        Console.WriteLine($"[Echo] 插件启动，监听 {Message.GroupIds.Length} 个群");
    }

    private async void OnGroupMessage(GroupMessageEvent e)
    {
        // 业务逻辑：复读消息
        if (Message.GroupIds.Contains(e.GroupId) && e.RawMessage == "复读")
        {
            // 使用高度封装的 Sender 发送消息
            await Bot.Sender.SendGroupMessage(e.GroupId, "复读成功！");
        }
    }
}

```

### 插件配置文件示例

运行一次后，会自动在 `Plug/` 目录下生成 `EchoPlugin.json`：

```json
{
  "Message": {
    "PlugIsOpen": true,
    "PlugName": "插件名称",
    "Description": "插件描述"
  },
  "GroupIds": []
}

```

---

## 🛠️ 核心 API 说明

### `BotActionSender`

每个机器人连接都有一个独立的 Sender，支持以下快捷操作：

* `SendGroupMessage(groupId, msg)`: 发送群消息
* `SendPrivateMessage(userId, msg)`: 发送私聊
* `SendGroupImage(groupId, url/path)`: 发送群图片
* `SendGroupMixedMessage(...)`: 发送图文混合消息
* `DeleteMessage(msgId)`: 撤回消息

---

## 🤝 贡献与交流

欢迎提交 Issue 或 Pull Request 来改进 BkyhBot！

---

## 📄 开源协议

本项目采用 [MIT License](https://www.google.com/search?q=LICENSE) 开源。

```

### 使用建议：
1.  **复制内容**：将上面的代码块直接复制到你项目根目录的 `README.md` 文件中。
2.  **修改链接**：如果你的 GitHub 仓库地址确定了，可以把 Badge 里的链接换成真实的仓库地址。
3.  **补充图片**：你可以截一张你的机器人运行时的控制台日志截图，或者机器人回复消息的截图，放在 README 里，会不仅让项目看起来更专业，也能直观展示功能。

```