using BrowserAutomation;
using BrowserAPI.Models;
using Jsonsee;
using Microsoft.AspNetCore.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BrowserAPI.Services
{
    /// <summary>
    /// 聊天服务 - 处理所有聊天相关的业务逻辑
    /// 包括AI通信、工具调用、历史记录管理、SSE通知等
    /// </summary>
    public class ChatService
    {
        private readonly McpBrowserService _browserService;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly HttpContext? _httpContext;
        private static readonly string _historyFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "chat_history.json");
        private static readonly object _fileLock = new object();

        /// <summary>
        /// 初始化聊天服务
        /// </summary>
        /// <param name="browserService">浏览器自动化服务</param>
        /// <param name="httpClientFactory">HTTP客户端工厂</param>
        /// <param name="configuration">配置服务</param>
        /// <param name="httpContextAccessor">HTTP上下文访问器</param>
        public ChatService(
            McpBrowserService browserService,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            IHttpContextAccessor? httpContextAccessor = null)
        {
            _browserService = browserService;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _httpContext = httpContextAccessor?.HttpContext;
        }

        #region AI配置

        /// <summary>
        /// 获取AI服务器基础URL
        /// </summary>
        /// <returns>AI服务器URL</returns>
        public string GetAiBaseUrl() => _configuration.GetValue<string>("AI:BaseUrl") ?? "http://localhost:11434";

        /// <summary>
        /// 获取AI模型名称
        /// </summary>
        /// <returns>模型名称</returns>
        public string GetAiModel() => _configuration.GetValue<string>("AI:Model") ?? "qwen/qwen3.5-35b-a3b";

        #endregion

        #region 工具定义

        /// <summary>
        /// 获取所有可用工具的定义列表
        /// </summary>
        /// <returns>工具定义列表</returns>
        public List<Tools> GetAvailableTools()
        {
            var tools = new List<Tools>();

            tools.Add(HelperSpeckmode.CreateTool(
                name: "navigate_to_url",
                description: "导航到指定的URL",
                properties: new Dictionary<string, ParameterProperty>
                {
                    ["url"] = new ParameterProperty { Type = "string", Description = "要导航到的URL" }
                },
                required: new List<string> { "url" }
            ));

            tools.Add(HelperSpeckmode.CreateTool(
                name: "get_arian_snapshot",
                description: "获取当前页面的Arian快照（可访问性树）",
                properties: new Dictionary<string, ParameterProperty>
                {
                    ["flatten"] = new ParameterProperty { Type = "boolean", Description = "是否扁平化输出" },
                    ["interesting_only"] = new ParameterProperty { Type = "boolean", Description = "是否只包含有意义的节点" }
                },
                required: new List<string>()
            ));

            tools.Add(HelperSpeckmode.CreateTool(
                name: "type_text",
                description: "在指定的输入框中输入文本",
                properties: new Dictionary<string, ParameterProperty>
                {
                    ["role"] = new ParameterProperty { Type = "string", Description = "元素角色" },
                    ["name"] = new ParameterProperty { Type = "string", Description = "元素名称" },
                    ["text"] = new ParameterProperty { Type = "string", Description = "要输入的文本" },
                    ["clear_first"] = new ParameterProperty { Type = "boolean", Description = "是否先清空输入框" }
                },
                required: new List<string> { "role", "name", "text" }
            ));

            tools.Add(HelperSpeckmode.CreateTool(
                name: "press_enter",
                description: "在指定元素上按下回车键",
                properties: new Dictionary<string, ParameterProperty>
                {
                    ["role"] = new ParameterProperty { Type = "string", Description = "元素角色" },
                    ["name"] = new ParameterProperty { Type = "string", Description = "元素名称" }
                },
                required: new List<string> { "role", "name" }
            ));

            tools.Add(HelperSpeckmode.CreateTool(
                name: "wait",
                description: "等待指定的毫秒数",
                properties: new Dictionary<string, ParameterProperty>
                {
                    ["milliseconds"] = new ParameterProperty { Type = "integer", Description = "等待的毫秒数" }
                },
                required: new List<string> { "milliseconds" }
            ));

            tools.Add(HelperSpeckmode.CreateTool(
                name: "take_screenshot",
                description: "截取当前页面的截图",
                properties: new Dictionary<string, ParameterProperty>
                {
                    ["full_page"] = new ParameterProperty { Type = "boolean", Description = "是否截取整个页面" }
                },
                required: new List<string>()
            ));

            tools.Add(HelperSpeckmode.CreateTool(
                name: "get_page_title",
                description: "获取当前页面的标题",
                properties: new Dictionary<string, ParameterProperty>(),
                required: new List<string>()
            ));

            tools.Add(HelperSpeckmode.CreateTool(
                name: "click_element",
                description: "点击指定角色和名称的元素",
                properties: new Dictionary<string, ParameterProperty>
                {
                    ["role"] = new ParameterProperty { Type = "string", Description = "元素角色" },
                    ["name"] = new ParameterProperty { Type = "string", Description = "元素名称" }
                },
                required: new List<string> { "role", "name" }
            ));

            tools.Add(HelperSpeckmode.CreateTool(
                name: "close_browser",
                description: "关闭浏览器并清理资源",
                properties: new Dictionary<string, ParameterProperty>(),
                required: new List<string>()
            ));

            return tools;
        }

        #endregion

        #region 工具执行器

        /// <summary>
        /// 执行指定的工具
        /// </summary>
        /// <param name="toolName">工具名称</param>
        /// <param name="argumentsJson">工具参数JSON字符串</param>
        /// <returns>工具执行结果</returns>
        public async Task<ReturnTools> ExecuteToolAsync(string toolName, string argumentsJson)
        {
            var result = new ReturnTools
            {
                Content = new List<ToolValue>(),
                IsError = false
            };

            try
            {
                using var doc = JsonDocument.Parse(argumentsJson);
                var root = doc.RootElement;

                switch (toolName.ToLower())
                {
                    case "navigate_to_url":
                        var url = root.GetProperty("url").GetString() ?? string.Empty;
                        var success = await _browserService.NavigateAsync(url);
                        result.Content.Add(new ToolValue { Type = "text", Text = success ? $"成功导航到: {url}" : $"导航失败: {url}" });
                        break;

                    case "get_arian_snapshot":
                        var flatten = root.TryGetProperty("flatten", out var flattenEl) ? flattenEl.GetBoolean() : true;
                        var interestingOnly = root.TryGetProperty("interesting_only", out var ioEl) ? ioEl.GetBoolean() : true;
                        var arian = await _browserService.GetArianSnapshotAsync(flatten, interestingOnly);
                        result.Content.Add(new ToolValue { Type = "text", Text = arian });
                        break;

                    case "type_text":
                        var typeRole = root.GetProperty("role").GetString() ?? string.Empty;
                        var typeName = root.GetProperty("name").GetString() ?? string.Empty;
                        var text = root.GetProperty("text").GetString() ?? string.Empty;
                        var clearFirst = root.TryGetProperty("clear_first", out var cfEl) ? cfEl.GetBoolean() : true;
                        var typeSuccess = await _browserService.TypeAsync(typeRole, typeName, text, clearFirst);
                        result.Content.Add(new ToolValue { Type = "text", Text = typeSuccess ? $"成功输入文本: {text}" : "输入失败" });
                        break;

                    case "press_enter":
                        var peRole = root.GetProperty("role").GetString() ?? string.Empty;
                        var peName = root.GetProperty("name").GetString() ?? string.Empty;
                        var peSuccess = await _browserService.PressEnter(peRole, peName);
                        result.Content.Add(new ToolValue { Type = "text", Text = peSuccess ? "成功按下回车键" : "按下回车键失败" });
                        break;

                    case "wait":
                        var ms = root.GetProperty("milliseconds").GetInt32();
                        await _browserService.WaitAsync(ms);
                        result.Content.Add(new ToolValue { Type = "text", Text = $"等待了 {ms} 毫秒" });
                        break;

                    case "take_screenshot":
                        var fullPage = root.TryGetProperty("full_page", out var fpEl) ? fpEl.GetBoolean() : true;
                        var base64Image = await _browserService.ScreenshotAsync(fullPage);
                        result.Content.Add(new ToolValue { Type = "text", Text = base64Image });
                        break;

                    case "get_page_title":
                        var title = await _browserService.GetTitleAsync();
                        result.Content.Add(new ToolValue { Type = "text", Text = title });
                        break;

                    case "click_element":
                        var clickRole = root.GetProperty("role").GetString() ?? string.Empty;
                        var clickName = root.GetProperty("name").GetString() ?? string.Empty;
                        var clickSuccess = await _browserService.ClickAsync(clickRole, clickName);
                        result.Content.Add(new ToolValue { Type = "text", Text = clickSuccess ? $"成功点击: {clickRole} - {clickName}" : "点击失败" });
                        break;

                    case "close_browser":
                        await _browserService.CloseAsync();
                        result.Content.Add(new ToolValue { Type = "text", Text = "浏览器已关闭" });
                        break;

                    default:
                        result.IsError = true;
                        result.Content.Add(new ToolValue { Type = "error", Error = $"未知工具: {toolName}" });
                        break;
                }
            }
            catch (Exception ex)
            {
                result.IsError = true;
                result.Content.Add(new ToolValue { Type = "error", Error = ex.Message });
            }

            return result;
        }

        #endregion

        #region 历史记录管理

        /// <summary>
        /// 保存对话历史到本地JSON文件
        /// </summary>
        /// <param name="conversation">对话内容</param>
        /// <returns>操作是否成功</returns>
        public bool SaveHistory(Speckjson conversation)
        {
            try
            {
                var history = new ChatHistory
                {
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now,
                    Conversation = conversation
                };

                lock (_fileLock)
                {
                    var json = JsonSerializer.Serialize(history, new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                    });
                    System.IO.File.WriteAllText(_historyFilePath, json);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 从本地JSON文件加载对话历史
        /// </summary>
        /// <returns>历史记录数据，如果不存在则返回null</returns>
        public ChatHistory? LoadHistory()
        {
            try
            {
                if (!System.IO.File.Exists(_historyFilePath))
                {
                    return null;
                }

                string json;
                lock (_fileLock)
                {
                    json = System.IO.File.ReadAllText(_historyFilePath);
                }

                return JsonSerializer.Deserialize<ChatHistory>(json);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 清除本地历史记录文件
        /// </summary>
        /// <returns>操作是否成功</returns>
        public bool ClearHistory()
        {
            try
            {
                if (System.IO.File.Exists(_historyFilePath))
                {
                    lock (_fileLock)
                    {
                        System.IO.File.Delete(_historyFilePath);
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region SSE通知

        /// <summary>
        /// 发送SSE事件到前端
        /// </summary>
        /// <param name="eventType">事件类型</param>
        /// <param name="data">事件数据</param>
        public async Task SendSseEventAsync(string eventType, object data)
        {
            if (_httpContext == null) return;

            var notification = new FrontendNotification
            {
                Type = eventType,
                Data = data,
                Timestamp = DateTime.Now
            };
            var json = JsonSerializer.Serialize(notification);
            await _httpContext.Response.WriteAsync($"data: {json}\n\n");
            await _httpContext.Response.Body.FlushAsync();
        }

        /// <summary>
        /// 设置SSE响应头
        /// </summary>
        public void SetSseResponseHeaders()
        {
            if (_httpContext == null) return;

            _httpContext.Response.ContentType = "text/event-stream";
            _httpContext.Response.Headers["Cache-Control"] = "no-cache";
            _httpContext.Response.Headers["Connection"] = "keep-alive";
        }

        #endregion

        #region HTTP客户端

        /// <summary>
        /// 创建HTTP客户端
        /// </summary>
        /// <returns>HTTP客户端实例</returns>
        public HttpClient CreateHttpClient()
        {
            return _httpClientFactory.CreateClient();
        }

        #endregion
    }
}
