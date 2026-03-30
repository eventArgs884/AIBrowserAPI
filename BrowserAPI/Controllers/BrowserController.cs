using BrowserAutomation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PuppeteerSharp;

namespace BrowserAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BrowserController : ControllerBase
    {
        private readonly McpBrowserService _browserService;
        private readonly IOptions<BrowserServiceOptions> _serviceOptions;

        public BrowserController(McpBrowserService browserService, IOptions<BrowserServiceOptions> serviceOptions)
        {
            _browserService = browserService;
            _serviceOptions = serviceOptions;
        }

        #region 请求模型定义
        public class ClickRequest
        {
            public string Role { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public int TimeoutMs { get; set; } = 8000;
        }

        public class PressEnterRequest
        {
            public string Role { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public int TimeoutMs { get; set; } = 5000;
        }

        public class TypeRequest
        {
            public string Role { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Text { get; set; } = string.Empty;
            public bool ClearFirst { get; set; } = true;
            public int TimeoutMs { get; set; } = 5000;
        }

        public class NavigateRequest
        {
            public string Url { get; set; } = string.Empty;
            public WaitUntilNavigation[]? WaitUntil { get; set; }
        }

        public class WaitRequest
        {
            public int Milliseconds { get; set; }
        }
        #endregion

        /// <summary>
        /// 获取浏览器服务配置选项
        /// </summary>
        [HttpGet("Options")]
        public IActionResult GetServiceOptions()
        {
            return Ok(_serviceOptions.Value);
        }

        /// <summary>
        /// 获取当前页面的可访问性树（Arian 快照）
        /// </summary>
        [HttpGet("Arian")]
        public async Task<IActionResult> GetArianSnapshotAsync([FromQuery] bool flatten = true, [FromQuery] bool interestingOnly = true)
        {
            var arian = await _browserService.GetArianSnapshotAsync(flatten, interestingOnly);
            return Ok(arian);
        }

        /// <summary>
        /// 点击指定角色和名称的元素
        /// </summary>
        [HttpPost("Click")]
        public async Task<IActionResult> ClickAsync([FromBody] ClickRequest request)
        {
            var success = await _browserService.ClickAsync(request.Role, request.Name, request.TimeoutMs);
            return success ? Ok() : BadRequest("未找到元素或点击失败");
        }

        /// <summary>
        /// 在指定元素上按下回车键
        /// </summary>
        [HttpPost("PressEnter")]
        public async Task<IActionResult> PressEnterAsync([FromBody] PressEnterRequest request)
        {
            var success = await _browserService.PressEnter(request.Role, request.Name, request.TimeoutMs);
            return success ? Ok() : BadRequest("未找到元素或按下回车失败");
        }

        /// <summary>
        /// 在指定输入框中输入文本
        /// </summary>
        [HttpPost("Type")]
        public async Task<IActionResult> TypeAsync([FromBody] TypeRequest request)
        {
            var success = await _browserService.TypeAsync(request.Role, request.Name, request.Text, request.ClearFirst, request.TimeoutMs);
            return success ? Ok() : BadRequest("未找到输入框或输入失败");
        }

        /// <summary>
        /// 截取当前页面截图并返回 Base64 字符串
        /// </summary>
        [HttpGet("Screenshot")]
        public async Task<IActionResult> ScreenshotAsync([FromQuery] bool fullPage = true)
        {
            var base64Image = await _browserService.ScreenshotAsync(fullPage);
            if (base64Image.StartsWith("Error:"))
                return BadRequest(base64Image);

            return Ok(new { Base64Image = base64Image });
        }

        /// <summary>
        /// 导航到指定 URL
        /// </summary>
        [HttpPost("Navigate")]
        public async Task<IActionResult> NavigateAsync([FromBody] NavigateRequest request)
        {
            var success = await _browserService.NavigateAsync(request.Url, request.WaitUntil);
            return success ? Ok() : BadRequest("导航失败");
        }

        /// <summary>
        /// 获取当前页面的 URL
        /// </summary>
        [HttpGet("CurrentUrl")]
        public async Task<IActionResult> GetCurrentUrlAsync()
        {
            var url = await _browserService.GetCurrentUrlAsync();
            return Ok(new { Url = url });
        }

        /// <summary>
        /// 获取当前页面的标题
        /// </summary>
        [HttpGet("Title")]
        public async Task<IActionResult> GetTitleAsync()
        {
            var title = await _browserService.GetTitleAsync();
            return Ok(new { Title = title });
        }

        /// <summary>
        /// 等待指定时间
        /// </summary>
        [HttpPost("Wait")]
        public async Task<IActionResult> WaitAsync([FromBody] WaitRequest request)
        {
            await _browserService.WaitAsync(request.Milliseconds);
            return Ok();
        }

        /// <summary>
        /// 关闭浏览器并清理资源
        /// </summary>
        [HttpPost("Close")]
        public async Task<IActionResult> CloseAsync()
        {
            await _browserService.CloseAsync();
            return Ok();
        }
    }
}