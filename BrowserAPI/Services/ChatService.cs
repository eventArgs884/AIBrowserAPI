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
        private static readonly string _historyFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "chat_histories");
        private static readonly string _currentHistoryFilePath = Path.Combine(_historyFolderPath, "current.json");
        private static readonly object _fileLock = new object();
        private static string? _currentHistoryId;

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
        /// 确保历史文件夹存在
        /// </summary>
        private void EnsureHistoryFolderExists()
        {
            if (!Directory.Exists(_historyFolderPath))
            {
                Directory.CreateDirectory(_historyFolderPath);
            }
        }

        /// <summary>
        /// 获取历史记录文件路径
        /// </summary>
        private string GetHistoryFilePath(string id)
        {
            return Path.Combine(_historyFolderPath, $"{id}.json");
        }

        /// <summary>
        /// 简化对话历史中的工具返回信息，减少文件大小
        /// </summary>
        /// <param name="conversation">原始对话内容</param>
        /// <returns>简化后的对话内容</returns>
        private Speckjson SimplifyConversationForStorage(Speckjson conversation)
        {
            var simplified = new Speckjson
            {
                Model = conversation.Model,
                Tools = conversation.Tools,
                ToolChoice = conversation.ToolChoice,
                Stream = conversation.Stream,
                Messages = new List<MessageList>()
            };

            foreach (var message in conversation.Messages)
            {
                var simplifiedMessage = new MessageList
                {
                    Role = message.Role,
                    Content = message.Content,
                    ToolCallId = message.ToolCallId,
                    ToolCalls = message.ToolCalls
                };

                // 如果是工具消息且内容很长，我们只保留一个简化版本
                if (message.Role == "tool" && message.Content is string contentStr)
                {
                    try
                    {
                        // 尝试解析为ReturnTools
                        var toolResult = JsonSerializer.Deserialize<ReturnTools>(contentStr);
                        if (toolResult != null)
                        {
                            // 创建简化版本
                            var simplifiedResult = new ReturnTools
                            {
                                Content = toolResult.Content?.Take(1).ToList(), // 只保留第一个
                                IsError = toolResult.IsError
                            };
                            simplifiedMessage.Content = JsonSerializer.Serialize(simplifiedResult, new JsonSerializerOptions
                            {
                                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                            });
                        }
                    }
                    catch
                    {
                        // 如果解析失败，保持原样
                    }
                }

                simplified.Messages.Add(simplifiedMessage);
            }

            return simplified;
        }

        /// <summary>
        /// 从对话内容生成标题
        /// </summary>
        private string GenerateTitleFromConversation(Speckjson conversation)
        {
            // 找到第一条用户消息作为标题
            foreach (var message in conversation.Messages)
            {
                if (message.Role == "user")
                {
                    if (message.Content is string contentStr)
                    {
                        // 取前30个字符作为标题
                        var title = contentStr.Length > 30 ? contentStr.Substring(0, 30) + "..." : contentStr;
                        return title;
                    }
                }
            }
            return "新对话";
        }

        /// <summary>
        /// 保存对话历史到指定ID的文件
        /// </summary>
        private bool SaveHistoryToFile(ChatHistory history)
        {
            try
            {
                EnsureHistoryFolderExists();
                var filePath = GetHistoryFilePath(history.Id);

                lock (_fileLock)
                {
                    var json = JsonSerializer.Serialize(history, new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                    });
                    System.IO.File.WriteAllText(filePath, json);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 保存当前对话历史
        /// </summary>
        public bool SaveHistory(Speckjson conversation)
        {
            try
            {
                var simplifiedConversation = SimplifyConversationForStorage(conversation);

                ChatHistory history;
                if (_currentHistoryId == null)
                {
                    history = new ChatHistory
                    {
                        Id = Guid.NewGuid().ToString(),
                        Title = GenerateTitleFromConversation(conversation),
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now,
                        Conversation = simplifiedConversation
                    };
                    _currentHistoryId = history.Id;
                }
                else
                {
                    history = LoadHistoryById(_currentHistoryId) ?? new ChatHistory
                    {
                        Id = _currentHistoryId,
                        CreatedAt = DateTime.Now
                    };
                    history.Title = GenerateTitleFromConversation(conversation);
                    history.UpdatedAt = DateTime.Now;
                    history.Conversation = simplifiedConversation;
                }

                return SaveHistoryToFile(history);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 加载当前对话历史
        /// </summary>
        public ChatHistory? LoadHistory()
        {
            if (_currentHistoryId != null)
            {
                return LoadHistoryById(_currentHistoryId);
            }
            return null;
        }

        /// <summary>
        /// 根据ID加载历史记录
        /// </summary>
        public ChatHistory? LoadHistoryById(string id)
        {
            try
            {
                var filePath = GetHistoryFilePath(id);
                if (!System.IO.File.Exists(filePath))
                {
                    return null;
                }

                string json;
                lock (_fileLock)
                {
                    json = System.IO.File.ReadAllText(filePath);
                }

                return JsonSerializer.Deserialize<ChatHistory>(json);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 获取所有历史记录摘要列表
        /// </summary>
        public List<ChatHistorySummary> GetHistoryList()
        {
            var summaries = new List<ChatHistorySummary>();
            try
            {
                EnsureHistoryFolderExists();
                var files = Directory.GetFiles(_historyFolderPath, "*.json")
                    .Where(f => !Path.GetFileName(f).Equals("current.json", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => System.IO.File.GetLastWriteTime(f));

                foreach (var file in files)
                {
                    try
                    {
                        var json = System.IO.File.ReadAllText(file);
                        var history = JsonSerializer.Deserialize<ChatHistory>(json);
                        if (history != null)
                        {
                            summaries.Add(new ChatHistorySummary
                            {
                                Id = history.Id,
                                Title = history.Title,
                                CreatedAt = history.CreatedAt,
                                UpdatedAt = history.UpdatedAt
                            });
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return summaries;
        }

        /// <summary>
        /// 创建新对话
        /// </summary>
        public ChatHistory CreateNewConversation()
        {
            SaveCurrentToHistory();
            _currentHistoryId = null;

            var aiModel = GetAiModel();
            var newHistory = new ChatHistory
            {
                Id = Guid.NewGuid().ToString(),
                Title = "新对话",
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now,
                Conversation = HelperSpeckmode.GetMode(aiModel, true)
            };

            _currentHistoryId = newHistory.Id;
            SaveHistoryToFile(newHistory);
            return newHistory;
        }

        /// <summary>
        /// 保存当前对话到历史记录
        /// </summary>
        private void SaveCurrentToHistory()
        {
            if (_currentHistoryId != null)
            {
                var currentHistory = LoadHistory();
                if (currentHistory != null)
                {
                    SaveHistoryToFile(currentHistory);
                }
            }
        }

        /// <summary>
        /// 加载指定的历史记录
        /// </summary>
        public ChatHistory? LoadHistoryByIdAndSetCurrent(string id)
        {
            SaveCurrentToHistory();
            var history = LoadHistoryById(id);
            if (history != null)
            {
                _currentHistoryId = id;
            }
            return history;
        }

        /// <summary>
        /// 删除指定ID的历史记录
        /// </summary>
        public bool DeleteHistory(string id)
        {
            try
            {
                var filePath = GetHistoryFilePath(id);
                if (System.IO.File.Exists(filePath))
                {
                    lock (_fileLock)
                    {
                        System.IO.File.Delete(filePath);
                    }
                    if (_currentHistoryId == id)
                    {
                        _currentHistoryId = null;
                    }
                    return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 清除本地历史记录文件
        /// </summary>
        public bool ClearHistory()
        {
            try
            {
                EnsureHistoryFolderExists();
                var files = Directory.GetFiles(_historyFolderPath, "*.json");
                lock (_fileLock)
                {
                    foreach (var file in files)
                    {
                        System.IO.File.Delete(file);
                    }
                }
                _currentHistoryId = null;
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
