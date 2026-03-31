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
        /// 历史记录唯一ID
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// 历史记录标题（由AI生成）
        /// </summary>
        public string Title { get; set; } = "新对话";

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

    /// <summary>
    /// 历史记录摘要信息
    /// </summary>
    public class ChatHistorySummary
    {
        /// <summary>
        /// 历史记录ID
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// 历史记录标题
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// 更新时间
        /// </summary>
        public DateTime UpdatedAt { get; set; }
    }
}
