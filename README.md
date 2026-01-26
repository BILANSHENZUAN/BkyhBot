# 📖 BkyhBot (BKYH 机器人驱动框架)

[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![OneBot](https://img.shields.io/badge/OneBot-v11-green.svg)](https://11.onebot.dev/)

**BkyhBot** 是一个专为 **NapCat/OneBot 11** 设计的轻量级 C# 机器人驱动框架。它采用先进的插件化架构，支持高度自定义的消息处理和动作执行，让你能以最优雅的代码实现最强大的功能！(๑•̀ㅂ•́)و✧

---

## 🛠️ 核心架构特色

### 1. 强化的单例插件系统
项目采用了全新的 **“泛型单例继承”** 模式。所有的插件逻辑类都继承自 `Plug<T, TA>`，确保每个插件在内存中全局唯一，避免了资源重复占用的问题。

### 2. 自动化配置生命周期
- **自动加载/生成**：插件启动时会自动在 `Plugins/` 目录下寻找对应的 JSON 配置文件。如果文件不存在，框架会根据配置类的默认值自动生成，实现真正的“零配置”上手。
- **智能编码**：内置了针对中文环境的优化，保存配置文件时不会出现乱码，方便欧尼酱直接手动修改 JSON。

### 3. 全面适配的强类型驱动
- **ID 系统升级**：为了兼容各种平台的特殊 ID，框架内的 QQ 号、群号等字段全面采用 `string` 类型。
- **多媒体深度集成**：内置封装了图片、语音、视频、回复、撤回等多种 OneBot 动作，支持 CQ 码自动构建。

---

## 💻 插件开发示例

得益于单例模式设计，开发一个新插件只需要简单的两步：

### 第一步：定义配置类
```csharp
// 插件特有的配置信息，需要继承 PlugMessage 基类
public class MyActionConfig : PlugMessage 
{
    // 业务特有参数，会自动序列化到 JSON
    public List<string> TargetGroups { get; set; } = new(); 
    public string ReplyText { get; set; } = "欧尼酱，我收到消息啦！";
}
第二步：编写业务逻辑
C#
// 继承 Plug<配置类, 插件类本身>
public class MyAction : Plug<MyActionConfig, MyAction>
{
    // 重写 OnInit 方法编写业务逻辑
    protected override void OnInit()
    {
        // 监听群消息事件
        Bot.OnGroupMessageReceived += async (e) => {
            // 使用继承自基类的开关检查
            if (!Message.PlugIsOpen) return;

            // 逻辑判断：如果群号在白名单内
            if (Message.TargetGroups.Contains(e.GroupId)) {
                await Bot.Sender.SendGroupMessage(e.GroupId, Message.ReplyText);
            }
        };
    }
}
🚀 快速开始
环境依赖：项目基于 .NET 10.0。

连接 NapCat：在 NapCat 中开启反向 WebSocket，连接地址指向框架监听的 URL（默认 http://127.0.0.1:3001/）。

启动代码：

C#
// 1. 初始化并启动机器人连接
var bot = new BotConnect(); 
await bot.Start(); //

// 2. 启动你的插件 (使用单例启动)
MyAction.Start(); //
📂 项目结构说明
BotConnect/: 核心连接管理，负责 WebSocket 握手、Token 校验与消息分发。

BotAction/: 动作执行器，封装了发送群/私聊消息、图片、撤回等 API。

Plugins/: 插件基类与配置持久化逻辑定义。

Class/: 包含 Config 全局配置、消息事件实体类及枚举定义。