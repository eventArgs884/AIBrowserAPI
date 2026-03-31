using BrowserAPI.Models;
using BrowserAPI.Services;
using Jsonsee;
using Microsoft.AspNetCore.Mvc;
using System.Text;

namespace BrowserAPI.Controllers
{
    /// <summary>
    /// 聊天控制器 - 提供AI对话、历史记录管理、工具调用等接口
    /// 作为中间层连接AI服务器和前端网页
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    public class ChatController : ControllerBase
    {
        private readonly ChatService _chatService;

        /// <summary>
        /// 初始化聊天控制器
        /// </summary>
        /// <param name="chatService">聊天服务</param>
        public ChatController(ChatService chatService)
        {
            _chatService = chatService;
        }

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
                var client = _chatService.CreateHttpClient();
                var aiBaseUrl = _chatService.GetAiBaseUrl();
                var aiModel = _chatService.GetAiModel();

                Speckjson? speckjson;
                if (request.LoadHistory)
                {
                    var history = _chatService.LoadHistory();
                    speckjson = history?.Conversation ?? HelperSpeckmode.GetMode(aiModel, request.Stream);
                }
                else
                {
                    speckjson = HelperSpeckmode.GetMode(aiModel, request.Stream);
                }

                speckjson.Tools = _chatService.GetAvailableTools();
                speckjson.ToolChoice = "auto";
                speckjson.Stream = request.Stream;

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
            var requestJson = HelperSpeckmode.GetJson(speckjson);
            var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

            var response = await client.PostAsync($"{aiBaseUrl}/v1/chat/completions", content);
            response.EnsureSuccessStatusCode();

            var stream = await response.Content.ReadAsStreamAsync();
            var reader = new StreamReader(stream);

            _chatService.SetSseResponseHeaders();

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
                                await _chatService.SendSseEventAsync("content", new { content = choice.Delta.Content });
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

                await _chatService.SendSseEventAsync("tool_calls", new { tool_calls = accumulatedToolCalls });

                foreach (var toolCall in accumulatedToolCalls)
                {
                    if (toolCall.Function?.Name != null && toolCall.Function?.Arguments != null)
                    {
                        var toolResult = await _chatService.ExecuteToolAsync(toolCall.Function.Name, toolCall.Function.Arguments);
                        HelperSpeckmode.AddValueMess(speckjson, HelperSpeckmode.Token.工具, toolResult, toolCall.Id ?? "");
                        await _chatService.SendSseEventAsync("tool_result", new { tool_name = toolCall.Function.Name, result = toolResult });
                    }
                }

                speckjson.Stream = true;
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
                                    await _chatService.SendSseEventAsync("content", new { content = choice.Delta.Content });
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

            _chatService.SaveHistory(speckjson);
            await _chatService.SendSseEventAsync("done", new { });

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
                        var toolResult = await _chatService.ExecuteToolAsync(toolCall.Function.Name, toolCall.Function.Arguments);
                        //处理一下图片
                        if (toolCall.Function.Name == "take_screenshot")
                        {
                            ReturnTools Im = new ReturnTools()
                            {
                                Content = new List<ToolValue>()
                                {
                                    new ToolValue()
                                    {
                                        Type="text",
                                        Text = "已经自动截图",
                                    }
                                }
                            };
                            if (toolResult.Content != null)
                            {
                                HelperSpeckmode.AddValueMess(speckjson, HelperSpeckmode.Token.工具, Im, toolCall.Id ?? "");
                                HelperSpeckmode.AddImageMess(speckjson, HelperSpeckmode.Token.用户, new List<string>() { toolResult.Content[0].Text ?? "" }, "自动截图");
                            }
                        }
                        else
                        {
                            HelperSpeckmode.AddValueMess(speckjson, HelperSpeckmode.Token.工具, toolResult, toolCall.Id ?? "");
                        }
                        toolResults.Add(new
                        {
                            tool_name = toolCall.Function.Name,
                            result = toolResult
                        });
                    }
                }

                speckjson.Stream = false;
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

                _chatService.SaveHistory(speckjson);
                return Ok(responseDict);
            }

            _chatService.SaveHistory(speckjson);
            return Ok(new { success = true, content = message?.Content });
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
            var success = _chatService.SaveHistory(conversation);
            if (success)
            {
                return Ok(new { success = true, message = "历史记录保存成功" });
            }
            return BadRequest(new { success = false, message = "历史记录保存失败" });
        }

        /// <summary>
        /// 从本地JSON文件加载对话历史
        /// </summary>
        /// <returns>历史记录数据</returns>
        [HttpGet("history/load")]
        public IActionResult LoadHistory()
        {
            var history = _chatService.LoadHistory();
            if (history == null)
            {
                return NotFound(new { success = false, message = "没有找到历史记录" });
            }
            return Ok(new { success = true, data = history });
        }

        /// <summary>
        /// 清除本地历史记录文件
        /// </summary>
        /// <returns>操作结果</returns>
        [HttpDelete("history/clear")]
        public IActionResult ClearHistory()
        {
            var success = _chatService.ClearHistory();
            if (success)
            {
                return Ok(new { success = true, message = "历史记录已清除" });
            }
            return BadRequest(new { success = false, message = "历史记录清除失败" });
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
            return Ok(new { success = true, tools = _chatService.GetAvailableTools() });
        }

        #endregion
    }
}
