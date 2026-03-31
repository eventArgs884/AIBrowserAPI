namespace BrowserAPI.Models
{
    /// <summary>
    /// 前端通知模型
    /// 用于通过SSE向客户端推送事件
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
}
