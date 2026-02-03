using BkyhBot.Class;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace BkyhBot.Web;

public class WebDashboard
{
	private static readonly ConcurrentQueue<string> _logCache = new();
	private const int MaxLogCount = 300; // 增加缓存量

	public static void AddLog(string log)
	{
		string timeLog = $"[{DateTime.Now:HH:mm:ss}] {log}";
		_logCache.Enqueue(timeLog);
		while (_logCache.Count > MaxLogCount) _logCache.TryDequeue(out _);
	}

	public static async Task StartAsync(Config config)
	{
		try
		{
			var options = new WebApplicationOptions { WebRootPath = "wwwroot", ContentRootPath = AppContext.BaseDirectory };
			var builder = WebApplication.CreateBuilder(options);
			builder.Logging.ClearProviders();
			var app = builder.Build();

			string url = config.WebDashboardUrl.Replace("*", "0.0.0.0");
			app.Urls.Add(url);
			app.UseDefaultFiles();
			app.UseStaticFiles();

			// API: 基本信息
			app.MapGet("/api/info", (HttpContext context) =>
			{
				if (!CheckAuth(context, config.WebAdminSecret)) return Results.Unauthorized();
				return Results.Json(new
				{
					BotName = config.Name, BotQQ = config.BotQq, MasterQQ = config.MasterQq,
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

			// API: 重启
			app.MapPost("/api/restart", async (HttpContext context) =>
			{
				if (!CheckAuth(context, config.WebAdminSecret)) return Results.Unauthorized();

				// 放入后台任务执行，防止阻塞 HTTP 响应
				_ = Task.Run(async () => await BotConnect.BotConnect.Instance.Restart());

				return Results.Ok(new { msg = "重启指令已下达，请观察日志窗口。" });
			});

			Console.WriteLine($"[Web] 网页端启动成功: {url}");
			await app.RunAsync();
		}
		catch (Exception ex)
		{
			Console.ForegroundColor = ConsoleColor.Red;
			Console.WriteLine($"[Web 错误] {ex.Message}");
			Console.ResetColor();
		}
	}

	private static bool CheckAuth(HttpContext context, string correctSecret)
	{
		string? auth = context.Request.Headers["Authorization"];
		return auth == correctSecret;
	}
}