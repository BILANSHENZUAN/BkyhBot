# BkyhBot - QQ机器人插件框架

## 项目简介
BkyhBot是一个基于.NET的QQ机器人插件框架，通过NapCat实现与QQ客户端的连接。提供高度模块化的插件系统，使开发者能够快速扩展机器人功能。

## NapCat对接指南

### 1. 前置条件
- 安装[NapCat](https://github.com/NapNeko/NapCat)客户端
- 确保NapCat版本与框架兼容

### 2. 配置对接参数
修改`Config.json`文件：
```json
{
  "BotQq": 123456789,       // 机器人QQ号
  "Url": "http://127.0.0.1:45999",  // 监听地址
  "Token": "your_token",    // 鉴权令牌(需与NapCat配置一致)
  "MasterQq": 987654321,    // 管理员QQ号
  "Name": "机器人名称"      // 机器人显示名称
}
```

### 3. NapCat配置
在NapCat的配置文件中设置：
```yaml
websocket:
  url: ws://127.0.0.1:45999/
  token: your_token  # 必须与Config.json中的Token一致
```

## 快速启动
1. **启动BkyhBot**
   ```bash
   dotnet run --project Bkyh
   ```

2. **启动NapCat**
   运行NapCat客户端并登录机器人QQ账号

3. **验证连接**
   - 控制台显示"机器人[QQ号]已接入"表示连接成功
   - 向机器人发送消息测试功能

## 插件开发指南

### 1. 创建插件
```csharp
public class MyPlugin : IPlugin 
{
    public string Name => "我的插件";
    
    public void Initialize(PluginContext context) 
    {
        // 初始化逻辑
    }
    
    public Task HandleMessage(MessageContext context) 
    {
        // 消息处理逻辑
        return Task.CompletedTask;
    }
}
```

### 2. 插件配置
在`PlugConfig`目录创建对应的JSON配置文件：
```json
{
  "enable": true,
  "configKey": "value"
}
```

### 3. 消息处理示例
```csharp
public Task HandleMessage(MessageContext context)
{
    if(context.Message == "测试")
    {
        context.Reply("收到测试消息");
    }
    return Task.CompletedTask;
}
```

## 常用功能示例

### 发送消息
```csharp
// 发送群消息
await context.SendGroupMessage(groupId, "Hello World");

// 发送私聊消息 
await context.SendPrivateMessage(userId, "Hello");
```

### 处理图片消息
```csharp
if(context.MessageType == MessageType.Image)
{
    string imageUrl = context.GetImageUrl();
    // 处理图片逻辑
}
```

## 问题排查
1. **连接失败**
   - 检查NapCat和BkyhBot的Token是否一致
   - 确认端口未被占用

2. **消息未响应**
   - 检查插件是否启用
   - 查看日志确认消息是否接收成功

## 贡献指南
欢迎提交Pull Request贡献代码，请遵循现有代码风格。
