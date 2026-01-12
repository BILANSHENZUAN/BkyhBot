# BkyhBot - QQ机器人快速开发框架

## 快速启动指南

### 1. 安装准备
```bash
# 克隆项目
git clone https://github.com/your-repo/BkyhBot.git
cd BkyhBot

# 安装.NET 10.0 SDK
```

### 2. 配置NapCat连接
修改`Bkyh/Config.json`：
```json
{
  "BotQq": 123456789,
  "Url": "http://127.0.0.1:45999",
  "Token": "your_token",
  "MasterQq": 987654321,
  "Name": "机器人名称"
}
```

### 3. 运行机器人
```bash
dotnet run --project Bkyh
```

## 快速开发教程

### 1. 创建基础插件
```csharp
// MyFirstPlugin.cs
public class MyFirstPlugin : IPlugin
{
    public string Name => "我的第一个插件";
    
    public void Initialize(PluginContext context)
    {
        Console.WriteLine($"{Name}已加载");
    }
    
    public Task HandleMessage(MessageContext context)
    {
        if(context.Message == "你好")
        {
            context.Reply("你好，我是BkyhBot！");
        }
        return Task.CompletedTask;
    }
}
```

### 2. 注册插件
在Program.cs中添加：
```csharp
// 在StartFinish方法中添加
var myPlugin = new MyFirstPlugin();
myPlugin.Initialize(new PluginContext(_botConnect));
```

## 插件开发进阶

### 1. 插件配置
创建`PlugConfig/MyPlugin.json`：
```json
{
  "enable": true,
  "replyText": "这是配置的回复消息"
}
```

### 2. 使用配置
```csharp
public class MyPlugin : IPlugin
{
    private JObject _config;
    
    public void Initialize(PluginContext context)
    {
        _config = JObject.Parse(File.ReadAllText("PlugConfig/MyPlugin.json"));
    }
    
    public Task HandleMessage(MessageContext context)
    {
        if(_config["enable"].Value<bool>())
        {
            context.Reply(_config["replyText"].ToString());
        }
        return Task.CompletedTask;
    }
}
```

## 核心API参考

### 消息发送
```csharp
// 发送群消息
await context.SendGroupMessage(groupId, "Hello");

// 发送图片
await context.SendGroupImage(groupId, "图片标题", "path/to/image.jpg");
```

### 消息接收
```csharp
public Task HandleMessage(MessageContext context)
{
    // 消息类型判断
    if(context.MessageType == MessageType.Text)
    {
        // 文本消息处理
    }
    else if(context.MessageType == MessageType.Image)
    {
        // 图片消息处理
    }
    return Task.CompletedTask;
}
```

## 示例插件

### 复读机插件
```csharp
public class EchoPlugin : IPlugin
{
    public Task HandleMessage(MessageContext context)
    {
        // 复读用户消息
        context.Reply(context.Message);
        return Task.CompletedTask;
    }
}
```

### 管理员指令
```csharp
public class AdminPlugin : IPlugin
{
    public Task HandleMessage(MessageContext context)
    {
        if(context.UserId == context.Config.MasterQq)
        {
            if(context.Message == "重启")
            {
                // 执行重启逻辑
            }
        }
        return Task.CompletedTask;
    }
}
```

## 调试技巧
1. 查看控制台日志
2. 使用`OnLog`事件记录运行信息
3. 检查NapCat连接状态

## 完整开发流程
1. 创建插件类实现`IPlugin`接口
2. 在`PlugConfig`添加配置文件
3. 在`Program.cs`中注册插件
4. 测试并调试功能
5. 提交Pull Request贡献代码
