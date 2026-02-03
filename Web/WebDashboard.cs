using BkyhBot.Class;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace BkyhBot.Web;

public class WebDashboard
{
	// 用于缓存日志
	private static readonly ConcurrentQueue<string> _logCache = new();
	private const int MaxLogCount = 100;

	/// <summary>
	/// 记录日志供网页端读取
	/// </summary>
	public static void AddLog(string log)
	{
		string timeLog = $"[{DateTime.Now:HH:mm:ss}] {log}";
		_logCache.Enqueue(timeLog);
		// 限制日志数量防止内存溢出
		while (_logCache.Count > MaxLogCount)
		{
			_logCache.TryDequeue(out _);
		}
	}

	/// <summary>
	/// 启动网页服务器
	/// </summary>
	public static async Task StartAsync(Config config)
	{
		try
		{
			// ================== 1. 路径环境调试 ==================
			// 获取程序实际运行的目录（bin\Debug\net10.0\）
			string baseDir = AppContext.BaseDirectory;
			// 拼接出预期的网页文件目录
			string webRoot = Path.Combine(baseDir, "wwwroot");

			Console.ForegroundColor = ConsoleColor.Yellow;
			Console.WriteLine($"[Web 调试] 程序运行目录: {baseDir}");
			Console.WriteLine($"[Web 调试] 网页资源目录: {webRoot}");

			// 检查文件夹和文件是否存在
			if (!Directory.Exists(webRoot))
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine($"[Web 调试] ❌ 严重错误: 找不到 wwwroot 文件夹！");
				Console.WriteLine($"[Web 调试] 💡 请检查 BkyhBot.csproj 是否添加了 <Content> 复制指令！");
			}
			else if (!File.Exists(Path.Combine(webRoot, "index.html")))
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine($"[Web 调试] ❌ 错误: wwwroot 文件夹存在，但里面没有 index.html 文件！");
			}
			else
			{
				Console.ForegroundColor = ConsoleColor.Green;
				Console.WriteLine($"[Web 调试] ✅ 成功找到网页文件，准备启动服务。");
			}

			Console.ResetColor();
			// ===================================================

			Console.WriteLine($"[Web] 正在启动网页端，监听: {config.WebDashboardUrl}");

			// ================== 2. 创建 Web 应用 ==================
			// 使用 WebApplicationOptions 显式指定根目录，防止找不到文件
			var options = new WebApplicationOptions
			{
				WebRootPath = "wwwroot", // 告诉它静态文件在 wwwroot 文件夹里
				ContentRootPath = baseDir // 告诉它根目录是当前运行目录
			};

			var builder = WebApplication.CreateBuilder(options);

			// 清除默认的一大堆日志，只保留我们要的
			builder.Logging.ClearProviders();

			var app = builder.Build();

			// ================== 3. 配置监听地址 ==================
			string url = config.WebDashboardUrl;
			// 自动修正 *:5000 为 0.0.0.0:5000 以避免格式错误
			if (url.Contains("*"))
			{
				url = url.Replace("*", "0.0.0.0");
			}

			app.Urls.Add(url);

			// ================== 4. 开启功能模块 ==================
			app.UseDefaultFiles(); // 允许访问 / 时自动跳转 index.html
			app.UseStaticFiles(); // 允许下载 css/js/html 文件

			// API: 获取基本信息
			app.MapGet("/api/info", (HttpContext context) =>
			{
				if (!CheckAuth(context, config.WebAdminSecret)) return Results.Unauthorized();
				return Results.Json(new
				{
					BotName = config.Name,
					BotQQ = config.BotQq,
					MasterQQ = config.MasterQq,
					RunTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
					OS = Environment.OSVersion.ToString(),
					LogCount = _logCache.Count
				});
			});

			// API: 获取日志
			app.MapGet("/api/logs", (HttpContext context) =>
			{
				if (!CheckAuth(context, config.WebAdminSecret)) return Results.Unauthorized();
				return Results.Json(_logCache.ToArray());
			});

			Console.WriteLine($"[Web] 服务配置完成，请访问浏览器查看。");

			// 启动服务
			await app.RunAsync();
		}
		catch (Exception ex)
		{
			// ================== 5. 错误捕获 ==================
			Console.ForegroundColor = ConsoleColor.Red;
			Console.WriteLine($"\n[Web 致命错误] 网页控制台启动失败！！！");
			Console.WriteLine($"错误信息: {ex.Message}");
			Console.WriteLine($"堆栈追踪: {ex.StackTrace}");
			Console.ResetColor();
		}
	}

	/// <summary>
	/// 验证密钥
	/// </summary>
	private static bool CheckAuth(HttpContext context, string correctSecret)
	{
		string? auth = context.Request.Headers["Authorization"];
		return auth == correctSecret;
	}
}