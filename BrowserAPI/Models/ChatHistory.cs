using Jsonsee;

namespace BrowserAPI.Models
{
    /// <summary>
    /// 聊天历史记录模型
    /// 用于存储和恢复对话历史
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
}
