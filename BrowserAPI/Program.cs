using BrowserAutomation;

var builder = WebApplication.CreateBuilder(args);

// 配置 BrowserServiceOptions
builder.Services.Configure<BrowserServiceOptions>(builder.Configuration.GetSection("BrowserSettings"));

// 注册 McpBrowserService
builder.Services.AddSingleton<McpBrowserService>();

// 注册 HttpClientFactory
builder.Services.AddHttpClient();

builder.Services.AddControllers();

var app = builder.Build();

//app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();