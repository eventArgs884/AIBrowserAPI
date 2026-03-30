using System;
using System.Collections.Generic;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jsonsee
{
    #region 核心请求与消息模型

    /// <summary>
    /// AI API 请求的最外层包裹类
    /// </summary>
    public class Speckjson
    {
        /// <summary>
        /// 要调用的模型名称 (如 "gpt-4", "claude-3")
        /// </summary>
        [JsonPropertyName("model")]
        [JsonRequired]
        public required string Model { get; set; }

        /// <summary>
        /// 对话消息列表 (上下文)
        /// </summary>
        [JsonPropertyName("messages")]
        [JsonRequired]
        public required List<MessageList> Messages { get; set; }

        /// <summary>
        /// 可用工具列表 (可选)
        /// </summary>
        [JsonPropertyName("tools")]
        public List<Tools>? Tools { get; set; }

        /// <summary>
        /// 工具选择模式 (如 "auto", "none", "required")
        /// </summary>
        [JsonPropertyName("tool_choice")]
        public string? ToolChoice { get; set; }

        /// <summary>
        /// 是否启用流式生成
        /// </summary>
        [JsonPropertyName("stream")]
        [JsonRequired]
        public required bool Stream { get; set; }
    }

    /// <summary>
    /// 单条对话消息的包裹类
    /// </summary>
    public class MessageList
    {
        /// <summary>
        /// 角色 (system, user, assistant, tool)
        /// </summary>
        [JsonPropertyName("role")]
        [JsonRequired]
        public required string Role { get; set; }

        /// <summary>
        /// 消息内容 (可以是 string 文本或 List[ImageContent] 多模态内容)
        /// </summary>
        [JsonPropertyName("content")]
        public object? Content { get; set; }

        /// <summary>
        /// 工具调用结果对应的 ID (仅 tool 角色消息使用)
        /// </summary>
        [JsonPropertyName("tool_call_id")]
        public string? ToolCallId { get; set; }

        /// <summary>
        /// 助手发起的工具调用列表 (仅 assistant 角色消息使用)
        /// </summary>
        [JsonPropertyName("tool_calls")]
        public List<ToolCall>? ToolCalls { get; set; }
    }

    /// <summary>
    /// 多模态内容项 (文本或图片)
    /// </summary>
    public class ImageContent
    {
        /// <summary>
        /// 类型 ("text" 或 "image_url")
        /// </summary>
        [JsonPropertyName("type")]
        [JsonRequired]
        public required string Type { get; set; }

        /// <summary>
        /// 文本内容 (当 Type 为 "text" 时必填)
        /// </summary>
        [JsonPropertyName("text")]
        public string? Text { get; set; }

        /// <summary>
        /// 图片URL (当 Type 为 "image_url" 时必填)
        /// </summary>
        [JsonPropertyName("image_url")]
        public ImageUrl? ImageUrl { get; set; }
    }

    /// <summary>
    /// 图片URL容器
    /// </summary>
    public class ImageUrl
    {
        /// <summary>
        /// 图片的具体URL地址或Base64数据
        /// </summary>
        [JsonPropertyName("url")]
        [JsonRequired]
        public required string Url { get; set; }
    }

    #endregion

    #region 工具调用相关模型

    /// <summary>
    /// 助手发起的单次工具调用
    /// </summary>
    public class ToolCall
    {
        /// <summary>
        /// 本次工具调用的唯一ID
        /// </summary>
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        /// <summary>
        /// 类型 (目前固定为 "function")
        /// </summary>
        [JsonPropertyName("type")]
        [JsonRequired]
        public required string Type { get; set; }

        /// <summary>
        /// 具体的函数调用详情
        /// </summary>
        [JsonPropertyName("function")]
        [JsonRequired]
        public required Function Function { get; set; }
    }

    /// <summary>
    /// 函数定义 (用于工具定义或工具调用请求)
    /// </summary>
    public class Function
    {
        /// <summary>
        /// 函数/工具名称
        /// </summary>
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        /// <summary>
        /// 调用参数 (JSON字符串格式，由AI生成)
        /// </summary>
        [JsonPropertyName("arguments")]
        public string? Arguments { get; set; }

        /// <summary>
        /// 函数功能描述 (告诉AI这个工具是做什么的)
        /// </summary>
        [JsonPropertyName("description")]
        public string? Description { get; set; }

        /// <summary>
        /// 函数参数结构定义 (JSON Schema)
        /// </summary>
        [JsonPropertyName("parameters")]
        public Parameters? Parameters { get; set; }
    }

    /// <summary>
    /// 函数参数结构定义 (JSON Schema)
    /// </summary>
    public class Parameters
    {
        /// <summary>
        /// 类型 (通常固定为 "object")
        /// </summary>
        [JsonPropertyName("type")]
        [JsonRequired]
        public required string Type { get; set; } = "object"; // 默认设为 object

        /// <summary>
        /// 参数属性定义 (强类型字典)
        /// </summary>
        [JsonPropertyName("properties")]
        [JsonRequired]
        public required Dictionary<string, ParameterProperty> Properties { get; set; }

        /// <summary>
        /// 必填参数名称列表
        /// </summary>
        [JsonPropertyName("required")]
        public List<string> Required { get; set; } = new List<string>(); // 默认为空列表

        /// <summary>
        /// 是否允许传入未在 properties 中定义的参数
        /// </summary>
        [JsonPropertyName("additionalProperties")]
        public bool AdditionalProperties { get; set; } = false; // 默认禁止额外属性
    }

    /// <summary>
    /// 单个参数的详细定义
    /// </summary>
    public class ParameterProperty
    {
        /// <summary>
        /// 参数类型 (string, integer, number, boolean, array, object)
        /// </summary>
        [JsonPropertyName("type")]
        [JsonRequired]
        public required string Type { get; set; }

        /// <summary>
        /// 参数描述
        /// </summary>
        [JsonPropertyName("description")]
        public string? Description { get; set; }

        /// <summary>
        /// 参数示例 (可选)
        /// </summary>
        [JsonPropertyName("examples")]
        public List<object>? Examples { get; set; } 
    }

    /// <summary>
    /// 工具定义 (用于 Request 中告诉 AI 有哪些工具可用)
    /// </summary>
    public class Tools
    {
        /// <summary>
        /// 类型 (目前固定为 "function")
        /// </summary>
        [JsonPropertyName("type")]
        [JsonRequired]
        public required string Type { get; set; }

        /// <summary>
        /// 函数的具体定义
        /// </summary>
        [JsonPropertyName("function")]
        [JsonRequired]
        public required Function Function { get; set; }
    }

    #endregion

    #region 工具返回值模型 (按要求修正)

    /// <summary>
    /// 工具执行结果的包裹类
    /// </summary>
    public class ReturnTools
    {
        /// <summary>
        /// 返回的内容列表
        /// </summary>
        [JsonPropertyName("content")]
        public List<ToolValue>? Content { get; set; }

        /// <summary>
        /// 工具执行是否出错
        /// </summary>
        [JsonPropertyName("isError")]
        public bool IsError { get; set; }
    }

    /// <summary>
    /// 工具返回的单个值 (仅支持文本、数字、错误，不支持图片)
    /// </summary>
    public class ToolValue
    {
        /// <summary>
        /// 类型 ("text", "number", "error")
        /// </summary>
        [JsonPropertyName("type")]
        [JsonRequired]
        public required string Type { get; set; }

        /// <summary>
        /// 文本内容 (Type 为 "text" 时使用)
        /// </summary>
        [JsonPropertyName("text")]
        public string? Text { get; set; }

        /// <summary>
        /// 数字内容 (Type 为 "number" 时使用)
        /// </summary>
        [JsonPropertyName("number")]
        public double? Number { get; set; }

        /// <summary>
        /// 错误信息 (Type 为 "error" 时使用)
        /// </summary>
        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }

    #endregion

    #region 辅助操作类

    /// <summary>
    /// 提供便捷操作 AI 请求模型的静态辅助类
    /// </summary>
    public static class HelperSpeckmode
    {
        /// <summary>
        /// 用于构建工具调用请求的临时参数容器
        /// </summary>
        public class ToolArg
        {
            /// <summary>
            /// 工具调用ID
            /// </summary>
            public required string Id { get; set; }

            /// <summary>
            /// 工具/函数名称
            /// </summary>
            public required string Name { get; set; }

            /// <summary>
            /// 参数字符串 (JSON格式)
            /// </summary>
            public required string Arguments { get; set; }
        }

        /// <summary>
        /// 消息角色枚举
        /// </summary>
        public enum Token
        {
            /// <summary>
            /// 用户消息
            /// </summary>
            用户,
            /// <summary>
            /// 助手消息
            /// </summary>
            助手,
            /// <summary>
            /// 工具消息
            /// </summary>
            工具,
            /// <summary>
            /// 系统提示词
            /// </summary>
            系统
        }

        private static string GetTokenValue(Token token) => token switch
        {
            Token.用户 => "user",
            Token.助手 => "assistant",
            Token.系统 => "system",
            Token.工具 => "tool",
            _ => throw new ArgumentOutOfRangeException(nameof(token), token, null)
        };

        /// <summary>
        /// 添加一条包含工具调用的消息 (通常用于记录 Assistant 的历史消息)
        /// </summary>
        /// <param name="speckjson">请求对象</param>
        /// <param name="token">角色</param>
        /// <param name="value">文本内容</param>
        /// <param name="toolargs">工具调用列表</param>
        public static Speckjson AddValueMess(Speckjson speckjson, Token token, string value, List<ToolArg> toolargs)
        {
            string juese = GetTokenValue(token);
            var toolCalls = new List<ToolCall>();

            toolargs.ForEach(toolarg =>
            {
                toolCalls.Add(new ToolCall
                {
                    Id = toolarg.Id,
                    Type = "function",
                    Function = new Function
                    {
                        Name = toolarg.Name,
                        Arguments = toolarg.Arguments
                    }
                });
            });

            speckjson.Messages.Add(new MessageList { Role = juese, Content = value, ToolCalls = toolCalls });
            return speckjson;
        }

        /// <summary>
        /// 添加一条工具返回结果的消息
        /// </summary>
        /// <param name="speckjson">请求对象</param>
        /// <param name="token">角色 (应为 Token.工具)</param>
        /// <param name="value">工具返回结果对象</param>
        /// <param name="toolCallId">对应的工具调用ID</param>
        public static Speckjson AddValueMess(Speckjson speckjson, Token token, ReturnTools value, string toolCallId)
        {
            string juese = GetTokenValue(token);
            var options = new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            string jsonValue = JsonSerializer.Serialize(value, options);
            speckjson.Messages.Add(new MessageList { Role = juese, Content = jsonValue, ToolCallId = toolCallId });
            return speckjson;
        }

        /// <summary>
        /// 添加一条纯文本消息
        /// </summary>
        /// <param name="speckjson">请求对象</param>
        /// <param name="token">角色</param>
        /// <param name="value">文本内容</param>
        public static Speckjson AddValueMess(Speckjson speckjson, Token token, string value)
        {
            string juese = GetTokenValue(token);
            speckjson.Messages.Add(new MessageList { Role = juese, Content = value });
            return speckjson;
        }

        /// <summary>
        /// 添加一条包含图片的多模态消息 (通常用于 User)
        /// </summary>
        /// <param name="speckjson">请求对象</param>
        /// <param name="token">角色</param>
        /// <param name="imageUrls">图片URL列表</param>
        /// <param name="value"> accompanying 文本</param>
        public static Speckjson AddImageMess(Speckjson speckjson, Token token, List<string> imageUrls, string value)
        {
            string juese = GetTokenValue(token);
            List<ImageContent> contents = new List<ImageContent>();

            if (!string.IsNullOrWhiteSpace(value))
            {
                contents.Add(new ImageContent
                {
                    Type = "text",
                    Text = value
                });
            }

            foreach (var url in imageUrls)
            {
                contents.Add(new ImageContent
                {
                    Type = "image_url",
                    ImageUrl = new ImageUrl { Url = url }
                });
            }

            speckjson.Messages.Add(new MessageList { Role = juese, Content = contents });
            return speckjson;
        }

        /// <summary>
        /// 将 JSON 字符串解析为指定的模型对象
        /// </summary>
        /// <typeparam name="T">目标类型</typeparam>
        /// <param name="json">JSON字符串</param>
        /// <returns>解析后的对象，失败返回 default</returns>
        public static T? GetModeJson<T>(string json)
        {
            var options = new JsonSerializerOptions
            {
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };
            try
            {
                return JsonSerializer.Deserialize<T>(json, options);
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"JSON 格式错误：{ex.Message}");
                return default;
            }
        }

        /// <summary>
        /// 创建一个标准的 AI 请求对象
        /// </summary>
        /// <param name="modelName">模型名称</param>
        /// <param name="stream">是否流式</param>
        public static Speckjson GetMode(string modelName, bool stream = true)
        {
            return new Speckjson
            {
                Model = modelName,
                Messages = new List<MessageList>(),
                Tools = new List<Tools>(),
                Stream = stream,
                ToolChoice = "auto"
            };
        }

        /// <summary>
        /// 创建一个带有预定义工具的 AI 请求对象
        /// </summary>
        /// <param name="modelName">模型名称</param>
        /// <param name="tools">工具列表</param>
        /// <param name="stream">是否流式</param>
        public static Speckjson GetMode(string modelName, List<Tools> tools, bool stream = true)
        {
            return new Speckjson
            {
                Model = modelName,
                Messages = new List<MessageList>(),
                Tools = tools,
                Stream = stream,
                ToolChoice = "auto"
            };
        }

        /// <summary>
        /// 将模型对象序列化为 JSON 字符串
        /// </summary>
        /// <typeparam name="T">对象类型</typeparam>
        /// <param name="obj">对象实例</param>
        public static string GetJson<T>(T obj)
        {
            var options = new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            return JsonSerializer.Serialize(obj, options);
        }

        /// <summary>
        /// 快速创建一个工具定义对象 (强类型版)
        /// </summary>
        /// <param name="name">工具名</param>
        /// <param name="description">功能描述</param>
        /// <param name="properties">强类型参数字典</param>
        /// <param name="required">必填参数名列表</param>
        public static Tools CreateTool(string name, string description, Dictionary<string, ParameterProperty> properties, List<string>? required = null)
        {
            return new Tools
            {
                Type = "function",
                Function = new Function
                {
                    Name = name,
                    Description = description,
                    Parameters = new Parameters
                    {
                        Type = "object",
                        Properties = properties,
                        Required = required ?? new List<string>(),
                        AdditionalProperties = false
                    }
                }
            };
        }
    }
    #endregion

    #region AI 响应模型

    /// <summary>
    /// AI 通用响应基类结构 (用于统一访问 Choices)
    /// </summary>
    public class AiGetTool
    {
        /// <summary>
        /// 生成的选择列表 (通常只有一个)
        /// </summary>
        [JsonPropertyName("choices")]
        [JsonRequired]
        public required List<Choices> Choices { get; set; }
    }

    /// <summary>
    /// AI 流式响应模型
    /// </summary>
    public class AiGetStream
    {
        /// <summary>
        /// 生成的选择列表
        /// </summary>
        [JsonPropertyName("choices")]
        [JsonRequired]
        public required List<Choices> Choices { get; set; }
    }

    /// <summary>
    /// AI 非流式响应模型
    /// </summary>
    public class AiGetNoStream
    {
        /// <summary>
        /// 生成的选择列表
        /// </summary>
        [JsonPropertyName("choices")]
        [JsonRequired]
        public required List<Choices> Choices { get; set; }
    }

    /// <summary>
    /// 单个生成选择项
    /// </summary>
    public class Choices
    {
        /// <summary>
        /// 结束原因 (null: 进行中, "stop": 结束, "tool_calls": 需要调用工具, "length": 超长, "content_filter": 拦截)
        /// </summary>
        [JsonPropertyName("finish_reason")]
        public string? FinishReason { get; set; }

        /// <summary>
        /// 流式增量内容 (仅流式响应有)
        /// </summary>
        [JsonPropertyName("delta")]
        public Delta? Delta { get; set; }

        /// <summary>
        /// 完整消息内容 (仅非流式响应有)
        /// </summary>
        [JsonPropertyName("message")]
        public Message? Message { get; set; }
    }

    /// <summary>
    /// 非流式响应的完整消息
    /// </summary>
    public class Message
    {
        /// <summary>
        /// 角色
        /// </summary>
        [JsonPropertyName("role")]
        public string? Role { get; set; }

        /// <summary>
        /// 文本内容
        /// </summary>
        [JsonPropertyName("content")]
        public string? Content { get; set; }

        /// <summary>
        /// 工具调用请求列表
        /// </summary>
        [JsonPropertyName("tool_calls")]
        public List<ToolCall>? ToolCalls { get; set; }
    }

    /// <summary>
    /// 流式响应的增量内容
    /// </summary>
    public class Delta
    {
        /// <summary>
        /// 增量工具调用 (可能是分片的)
        /// </summary>
        [JsonPropertyName("tool_calls")]
        public List<ToolCall>? ToolCalls { get; set; }

        /// <summary>
        /// 增量文本内容
        /// </summary>
        [JsonPropertyName("content")]
        public string? Content { get; set; }

        /// <summary>
        /// 推理内容 (部分模型的思考过程)
        /// </summary>
        [JsonPropertyName("reasoning_content")]
        public string? ReasoningContent { get; set; }
    }

    #endregion
}