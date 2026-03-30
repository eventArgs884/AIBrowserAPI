using BrowserAutomation;

var builder = WebApplication.CreateBuilder(args);


// 1. 将配置文件中的 "BrowserSettings" 节点绑定到 BrowserServiceOptions 类
builder.Services.Configure<BrowserServiceOptions>(builder.Configuration.GetSection("BrowserSettings"));

// 2. 注册 McpBrowserService
// 通常浏览器服务作为单例 (Singleton) 或 Scoped 注册，视你的具体需求而定
// 如果是为了长期持有浏览器实例，建议使用 Singleton
builder.Services.AddSingleton<McpBrowserService>();


builder.Services.AddControllers();

var app = builder.Build();

//app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();