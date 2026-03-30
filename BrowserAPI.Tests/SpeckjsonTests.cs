using Jsonsee;
using Xunit;

namespace BrowserAPI.Tests
{
    /// <summary>
    /// Speckjson模型测试类
    /// </summary>
    public class SpeckjsonTests
    {
        /// <summary>
        /// 测试创建Speckjson对象
        /// </summary>
        [Fact]
        public void CreateSpeckjson_ShouldInitializeCorrectly()
        {
            var model = "test-model";
            var stream = true;

            var speckjson = HelperSpeckmode.GetMode(model, stream);

            Assert.NotNull(speckjson);
            Assert.Equal(model, speckjson.Model);
            Assert.Equal(stream, speckjson.Stream);
            Assert.NotNull(speckjson.Messages);
            Assert.NotNull(speckjson.Tools);
        }

        /// <summary>
        /// 测试添加用户消息
        /// </summary>
        [Fact]
        public void AddUserMessage_ShouldAddToMessages()
        {
            var speckjson = HelperSpeckmode.GetMode("test-model", false);
            var message = "Hello, world!";

            HelperSpeckmode.AddValueMess(speckjson, HelperSpeckmode.Token.用户, message);

            Assert.Single(speckjson.Messages);
            Assert.Equal("user", speckjson.Messages[0].Role);
            Assert.Equal(message, speckjson.Messages[0].Content);
        }

        /// <summary>
        /// 测试创建工具定义
        /// </summary>
        [Fact]
        public void CreateTool_ShouldCreateValidTool()
        {
            var name = "test_tool";
            var description = "Test tool description";
            var properties = new Dictionary<string, ParameterProperty>
            {
                ["param1"] = new ParameterProperty { Type = "string", Description = "Parameter 1" }
            };
            var required = new List<string> { "param1" };

            var tool = HelperSpeckmode.CreateTool(name, description, properties, required);

            Assert.NotNull(tool);
            Assert.Equal("function", tool.Type);
            Assert.Equal(name, tool.Function.Name);
            Assert.Equal(description, tool.Function.Description);
            Assert.NotNull(tool.Function.Parameters);
            Assert.Contains("param1", tool.Function.Parameters.Properties.Keys);
        }

        /// <summary>
        /// 测试JSON序列化和反序列化
        /// </summary>
        [Fact]
        public void JsonSerialization_ShouldWorkCorrectly()
        {
            var speckjson = HelperSpeckmode.GetMode("test-model", false);
            HelperSpeckmode.AddValueMess(speckjson, HelperSpeckmode.Token.用户, "Test message");

            var json = HelperSpeckmode.GetJson(speckjson);
            var deserialized = HelperSpeckmode.GetModeJson<Speckjson>(json);

            Assert.NotNull(deserialized);
            Assert.Equal(speckjson.Model, deserialized.Model);
            Assert.Equal(speckjson.Stream, deserialized.Stream);
            Assert.Single(deserialized.Messages);
        }
    }
}
