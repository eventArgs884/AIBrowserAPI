namespace BrowserAPI.Models
{
    /// <summary>
    /// 聊天请求模型
    /// 用于接收前端发送的聊天请求
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
}
