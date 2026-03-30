using BrowserAPI.Controllers;
using Jsonsee;
using Xunit;

namespace BrowserAPI.Tests
{
    /// <summary>
    /// ChatController测试类
    /// </summary>
    public class ChatControllerTests
    {
        /// <summary>
        /// 测试ChatRequest模型初始化
        /// </summary>
        [Fact]
        public void ChatRequest_ShouldInitializeWithDefaults()
        {
            var request = new ChatController.ChatRequest();

            Assert.Equal(string.Empty, request.Message);
            Assert.True(request.Stream);
            Assert.True(request.LoadHistory);
        }

        /// <summary>
        /// 测试ChatHistory模型初始化
        /// </summary>
        [Fact]
        public void ChatHistory_ShouldInitializeCorrectly()
        {
            var speckjson = HelperSpeckmode.GetMode("test-model", false);
            var history = new ChatController.ChatHistory
            {
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now,
                Conversation = speckjson
            };

            Assert.NotNull(history.Conversation);
            Assert.Equal("test-model", history.Conversation.Model);
        }

        /// <summary>
        /// 测试FrontendNotification模型初始化
        /// </summary>
        [Fact]
        public void FrontendNotification_ShouldInitializeCorrectly()
        {
            var notification = new ChatController.FrontendNotification
            {
                Type = "content",
                Data = new { text = "test" },
                Timestamp = DateTime.Now
            };

            Assert.Equal("content", notification.Type);
            Assert.NotNull(notification.Data);
        }
    }
}
