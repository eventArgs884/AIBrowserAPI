using BrowserAutomation;
using Jsonsee;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BrowserAPI.Controllers
{
    /// <summary>
    /// 聊天控制器 - 提供AI对话、历史记录管理、工具调用等功能
    /// 作为中间层连接AI服务器和前端网页
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    public class ChatController : ControllerBase
    {
        private readonly McpBrowserService _browserService;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private static readonly string _historyFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "chat_history.json");
        private static readonly object _fileLock = new object();

        /// <summary>
        /// 初始化聊天控制器
        /// </summary>
        /// <param name="browserService">浏览器自动化服务</param>
        /// <param name="httpClientFactory">HTTP客户端工厂</param>
        /// <param name="configuration">配置服务</param>
        public ChatController(McpBrowserService browserService, IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            _browserService = browserService;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
        }

        #region 历史记录模型

        /// <summary>
        /// 聊天历史记录模型
        /// </summary>
        public class ChatHistory
        {
            /// <summary>
            /// 创建时间
            /// </summary>
            public DateTime CreatedAt { get; set; }
            /// <summary>
            /// 更新时间
            /// </summary>
            public DateTime UpdatedAt { get; set; }
            /// <summary>
            /// 对话内容
            /// </summary>
            public Speckjson Conversation { get; set; } = null!;
        }

        #endregion

        #region 前端通知模型

        /// <summary>
        /// 前端通知模型 - 用于通过SSE向客户端推送事件
        /// </summary>
        public class FrontendNotification
        {
            /// <summary>
            /// 事件类型：content, tool_calls, tool_result, done
            /// </summary>
            public string Type { get; set; } = string.Empty;
            /// <summary>
            /// 事件数据
            /// </summary>
            public object? Data { get; set; }
            /// <summary>
            /// 时间戳
            /// </summary>
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }

        #endregion

        #region AI配置

        /// <summary>
        /// 获取AI服务器基础URL
        /// </summary>
        /// <returns>AI服务器URL</returns>
        private string GetAiBaseUrl() => _configuration.GetValue<string>("AI:BaseUrl") ?? "http://localhost:11434";

        /// <summary>
        /// 获取AI模型名称
        /// </summary>
        /// <returns>模型名称</returns>
        private string GetAiModel() => _configuration.GetValue<string>("AI:Model") ?? "qwen/qwen3.5-35b-a3b";

        #endregion

        #region 工具定义

        /// <summary>
        /// 获取所有可用工具的定义列表
        /// </summary>
        /// <returns>工具定义列表</returns>
        private List<Tools> GetAvailableTools()
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
        private async Task<ReturnTools> ExecuteToolAsync(string toolName, string argumentsJson)
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
        /// <returns>操作结果</returns>
        [HttpPost("history/save")]
        public IActionResult SaveHistory([FromBody] Speckjson conversation)
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

                return Ok(new { success = true, message = "历史记录保存成功" });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// 从本地JSON文件加载对话历史
        /// </summary>
        /// <returns>历史记录数据</returns>
        [HttpGet("history/load")]
        public IActionResult LoadHistory()
        {
            try
            {
                if (!System.IO.File.Exists(_historyFilePath))
                {
                    return NotFound(new { success = false, message = "没有找到历史记录" });
                }

                string json;
                lock (_fileLock)
                {
                    json = System.IO.File.ReadAllText(_historyFilePath);
                }

                var history = JsonSerializer.Deserialize<ChatHistory>(json);
                return Ok(new { success = true, data = history });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// 清除本地历史记录文件
        /// </summary>
        /// <returns>操作结果</returns>
        [HttpDelete("history/clear")]
        public IActionResult ClearHistory()
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
                return Ok(new { success = true, message = "历史记录已清除" });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        #endregion

        #region 聊天接口

        /// <summary>
        /// 发送消息给AI并获取响应
        /// </summary>
        /// <param name="request">聊天请求</param>
        /// <returns>AI响应（流式或非流式）</returns>
        [HttpPost("send")]
        public async Task<IActionResult> SendMessage([FromBody] ChatRequest request)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                var aiBaseUrl = GetAiBaseUrl();
                var aiModel = GetAiModel();

                Speckjson? speckjson;
                if (request.LoadHistory && System.IO.File.Exists(_historyFilePath))
                {
                    string json;
                    lock (_fileLock)
                    {
                        json = System.IO.File.ReadAllText(_historyFilePath);
                    }
                    var history = JsonSerializer.Deserialize<ChatHistory>(json);
                    speckjson = history?.Conversation ?? HelperSpeckmode.GetMode(aiModel, request.Stream);
                }
                else
                {
                    speckjson = HelperSpeckmode.GetMode(aiModel, request.Stream);
                }

                speckjson.Tools = GetAvailableTools();
                speckjson.ToolChoice = "auto";

                if (!string.IsNullOrEmpty(request.Message))
                {
                    HelperSpeckmode.AddValueMess(speckjson, HelperSpeckmode.Token.用户, request.Message);
                }

                if (request.Stream)
                {
                    return await HandleStreamRequestAsync(client, aiBaseUrl, speckjson);
                }
                else
                {
                    return await HandleNonStreamRequestAsync(client, aiBaseUrl, speckjson);
                }
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// 处理流式请求
        /// </summary>
        /// <param name="client">HTTP客户端</param>
        /// <param name="aiBaseUrl">AI服务器URL</param>
        /// <param name="speckjson">请求对象</param>
        /// <returns>流式响应</returns>
        private async Task<IActionResult> HandleStreamRequestAsync(HttpClient client, string aiBaseUrl, Speckjson speckjson)
        {
            var response = new HttpResponseMessage();
            var requestJson = HelperSpeckmode.GetJson(speckjson);
            var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

            response = await client.PostAsync($"{aiBaseUrl}/v1/chat/completions", content);
            response.EnsureSuccessStatusCode();

            var stream = await response.Content.ReadAsStreamAsync();
            var reader = new StreamReader(stream);

            Response.ContentType = "text/event-stream";
            Response.Headers["Cache-Control"] = "no-cache";
            Response.Headers["Connection"] = "keep-alive";

            string? line;
            StringBuilder fullContent = new StringBuilder();
            List<ToolCall>? accumulatedToolCalls = null;
            string? finishReason = null;

            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (line.StartsWith("data: "))
                {
                    var data = line.Substring(6);
                    if (data == "[DONE]") break;

                    try
                    {
                        var streamResponse = HelperSpeckmode.GetModeJson<AiGetStream>(data);
                        if (streamResponse?.Choices != null && streamResponse.Choices.Count > 0)
                        {
                            var choice = streamResponse.Choices[0];
                            finishReason = choice.FinishReason;

                            if (choice.Delta?.Content != null)
                            {
                                fullContent.Append(choice.Delta.Content);
                                await SendSseEventAsync("content", new { content = choice.Delta.Content });
                            }

                            if (choice.Delta?.ToolCalls != null)
                            {
                                if (accumulatedToolCalls == null)
                                {
                                    accumulatedToolCalls = new List<ToolCall>();
                                }
                                foreach (var deltaToolCall in choice.Delta.ToolCalls)
                                {
                                    if (deltaToolCall.Index.HasValue)
                                    {
                                        while (accumulatedToolCalls.Count <= deltaToolCall.Index.Value)
                                        {
                                            accumulatedToolCalls.Add(new ToolCall { Type = "function", Function = new Function() });
                                        }
                                        var existing = accumulatedToolCalls[deltaToolCall.Index.Value];
                                        if (!string.IsNullOrEmpty(deltaToolCall.Id))
                                            existing.Id = deltaToolCall.Id;
                                        if (deltaToolCall.Function != null)
                                        {
                                            if (!string.IsNullOrEmpty(deltaToolCall.Function.Name))
                                                existing.Function.Name = deltaToolCall.Function.Name;
                                            existing.Function.Arguments += deltaToolCall.Function.Arguments ?? "";
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }
            }

            if (!string.IsNullOrEmpty(fullContent.ToString()))
            {
                HelperSpeckmode.AddValueMess(speckjson, HelperSpeckmode.Token.助手, fullContent.ToString());
            }

            if (finishReason == "tool_calls" && accumulatedToolCalls != null && accumulatedToolCalls.Count > 0)
            {
                var toolArgs = accumulatedToolCalls.Select(tc => new HelperSpeckmode.ToolArg
                {
                    Id = tc.Id ?? Guid.NewGuid().ToString(),
                    Name = tc.Function?.Name ?? "",
                    Arguments = tc.Function?.Arguments ?? "{}"
                }).ToList();

                if (!string.IsNullOrEmpty(fullContent.ToString()))
                {
                    HelperSpeckmode.AddValueMess(speckjson, HelperSpeckmode.Token.助手, fullContent.ToString(), toolArgs);
                }
                else
                {
                    speckjson.Messages.Add(new MessageList
                    {
                        Role = "assistant",
                        Content = "",
                        ToolCalls = accumulatedToolCalls
                    });
                }

                await SendSseEventAsync("tool_calls", new { tool_calls = accumulatedToolCalls });

                foreach (var toolCall in accumulatedToolCalls)
                {
                    if (toolCall.Function?.Name != null && toolCall.Function?.Arguments != null)
                    {
                        var toolResult = await ExecuteToolAsync(toolCall.Function.Name, toolCall.Function.Arguments);
                        HelperSpeckmode.AddValueMess(speckjson, HelperSpeckmode.Token.工具, toolResult, toolCall.Id ?? "");
                        await SendSseEventAsync("tool_result", new { tool_name = toolCall.Function.Name, result = toolResult });
                    }
                }

                var newRequestJson = HelperSpeckmode.GetJson(speckjson);
                var newContent = new StringContent(newRequestJson, Encoding.UTF8, "application/json");
                var newResponse = await client.PostAsync($"{aiBaseUrl}/v1/chat/completions", newContent);
                newResponse.EnsureSuccessStatusCode();

                var newStream = await newResponse.Content.ReadAsStreamAsync();
                var newReader = new StreamReader(newStream);
                StringBuilder newFullContent = new StringBuilder();

                while ((line = await newReader.ReadLineAsync()) != null)
                {
                    if (line.StartsWith("data: "))
                    {
                        var data = line.Substring(6);
                        if (data == "[DONE]") break;

                        try
                        {
                            var streamResponse = HelperSpeckmode.GetModeJson<AiGetStream>(data);
                            if (streamResponse?.Choices != null && streamResponse.Choices.Count > 0)
                            {
                                var choice = streamResponse.Choices[0];
                                if (choice.Delta?.Content != null)
                                {
                                    newFullContent.Append(choice.Delta.Content);
                                    await SendSseEventAsync("content", new { content = choice.Delta.Content });
                                }
                            }
                        }
                        catch { }
                    }
                }

                if (!string.IsNullOrEmpty(newFullContent.ToString()))
                {
                    HelperSpeckmode.AddValueMess(speckjson, HelperSpeckmode.Token.助手, newFullContent.ToString());
                }
            }

            SaveHistoryInternal(speckjson);
            await SendSseEventAsync("done", new { });

            return new EmptyResult();
        }

        /// <summary>
        /// 处理非流式请求
        /// </summary>
        /// <param name="client">HTTP客户端</param>
        /// <param name="aiBaseUrl">AI服务器URL</param>
        /// <param name="speckjson">请求对象</param>
        /// <returns>非流式响应</returns>
        private async Task<IActionResult> HandleNonStreamRequestAsync(HttpClient client, string aiBaseUrl, Speckjson speckjson)
        {
            var requestJson = HelperSpeckmode.GetJson(speckjson);
            var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

            var response = await client.PostAsync($"{aiBaseUrl}/v1/chat/completions", content);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            var aiResponse = HelperSpeckmode.GetModeJson<AiGetNoStream>(responseJson);

            if (aiResponse?.Choices == null || aiResponse.Choices.Count == 0)
            {
                return BadRequest(new { success = false, message = "AI没有返回有效响应" });
            }

            var choice = aiResponse.Choices[0];
            var message = choice.Message;

            if (message?.Content != null)
            {
                HelperSpeckmode.AddValueMess(speckjson, HelperSpeckmode.Token.助手, message.Content);
            }

            if (choice.FinishReason == "tool_calls" && message?.ToolCalls != null && message.ToolCalls.Count > 0)
            {
                var toolArgs = message.ToolCalls.Select(tc => new HelperSpeckmode.ToolArg
                {
                    Id = tc.Id ?? Guid.NewGuid().ToString(),
                    Name = tc.Function?.Name ?? "",
                    Arguments = tc.Function?.Arguments ?? "{}"
                }).ToList();

                if (message.Content != null)
                {
                    HelperSpeckmode.AddValueMess(speckjson, HelperSpeckmode.Token.助手, message.Content, toolArgs);
                }
                else
                {
                    speckjson.Messages.Add(new MessageList
                    {
                        Role = "assistant",
                        Content = "",
                        ToolCalls = message.ToolCalls
                    });
                }

                var responseDict = new Dictionary<string, object?>
                {
                    ["success"] = true,
                    ["content"] = message.Content,
                    ["tool_calls"] = message.ToolCalls,
                    ["tool_results"] = new List<object>()
                };

                var toolResults = (List<object>)responseDict["tool_results"]!;
                foreach (var toolCall in message.ToolCalls)
                {
                    if (toolCall.Function?.Name != null && toolCall.Function?.Arguments != null)
                    {
                        var toolResult = await ExecuteToolAsync(toolCall.Function.Name, toolCall.Function.Arguments);
                        HelperSpeckmode.AddValueMess(speckjson, HelperSpeckmode.Token.工具, toolResult, toolCall.Id ?? "");
                        toolResults.Add(new
                        {
                            tool_name = toolCall.Function.Name,
                            result = toolResult
                        });
                    }
                }

                var newRequestJson = HelperSpeckmode.GetJson(speckjson);
                var newContent = new StringContent(newRequestJson, Encoding.UTF8, "application/json");
                var newHttpResponse = await client.PostAsync($"{aiBaseUrl}/v1/chat/completions", newContent);
                newHttpResponse.EnsureSuccessStatusCode();

                var newResponseJson = await newHttpResponse.Content.ReadAsStringAsync();
                var newAiResponse = HelperSpeckmode.GetModeJson<AiGetNoStream>(newResponseJson);

                if (newAiResponse?.Choices != null && newAiResponse.Choices.Count > 0)
                {
                    var newMessage = newAiResponse.Choices[0].Message;
                    if (newMessage?.Content != null)
                    {
                        HelperSpeckmode.AddValueMess(speckjson, HelperSpeckmode.Token.助手, newMessage.Content);
                        responseDict["final_content"] = newMessage.Content;
                    }
                }

                SaveHistoryInternal(speckjson);
                return Ok(responseDict);
            }

            SaveHistoryInternal(speckjson);
            return Ok(new { success = true, content = message?.Content });
        }

        /// <summary>
        /// 发送SSE事件到前端
        /// </summary>
        /// <param name="eventType">事件类型</param>
        /// <param name="data">事件数据</param>
        private async Task SendSseEventAsync(string eventType, object data)
        {
            var notification = new FrontendNotification
            {
                Type = eventType,
                Data = data,
                Timestamp = DateTime.Now
            };
            var json = JsonSerializer.Serialize(notification);
            await Response.WriteAsync($"data: {json}\n\n");
            await Response.Body.FlushAsync();
        }

        /// <summary>
        /// 内部保存历史记录方法
        /// </summary>
        /// <param name="conversation">对话内容</param>
        private void SaveHistoryInternal(Speckjson conversation)
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
            }
            catch { }
        }

        /// <summary>
        /// 聊天请求模型
        /// </summary>
        public class ChatRequest
        {
            /// <summary>
            /// 用户发送的消息
            /// </summary>
            public string Message { get; set; } = string.Empty;
            /// <summary>
            /// 是否使用流式响应
            /// </summary>
            public bool Stream { get; set; } = true;
            /// <summary>
            /// 是否加载历史记录
            /// </summary>
            public bool LoadHistory { get; set; } = true;
        }

        #endregion

        #region 工具列表接口

        /// <summary>
        /// 获取所有可用工具列表
        /// </summary>
        /// <returns>工具列表</returns>
        [HttpGet("tools")]
        public IActionResult GetTools()
        {
            return Ok(new { success = true, tools = GetAvailableTools() });
        }

        #endregion
    }
}
