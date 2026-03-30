using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PuppeteerSharp;
using PuppeteerSharp.Input;
using PuppeteerSharp.PageAccessibility;

namespace BrowserAutomation
{
    /// <summary>
    /// 浏览器服务配置选项，定义浏览器启动及运行时的行为参数
    /// </summary>
    public class BrowserServiceOptions
    {
        /// <summary>
        /// 是否以无头模式启动浏览器（无界面模式，适合服务器环境）
        /// </summary>
        public bool Headless { get; set; } = false;

        /// <summary>
        /// Chrome/Chromium 可执行文件的相对或绝对路径
        /// </summary>
        public string ExecutablePath { get; set; } = @"chrome-win64\chrome.exe";

        /// <summary>
        /// 浏览器启动时的附加参数数组
        /// </summary>
        public string[] Args { get; set; } = new[] 
        {
            "--no-sandbox", 
            "--disable-setuid-sandbox", 
            "--disable-dev-shm-usage" 
        };

        /// <summary>
        /// 页面操作的默认超时时间（单位：毫秒）
        /// </summary>
        public int DefaultTimeout { get; set; } = 30000;

        /// <summary>
        /// 浏览器视口（Viewport）尺寸及设备特性配置
        /// </summary>
        public ViewPortOptions Viewport { get; set; } = new()
        {
            Width = 1920,
            Height = 1080,
            DeviceScaleFactor = 1,
            IsMobile = false,
            HasTouch = false
        };
    }

    /// <summary>
    /// 表示可访问性树（Accessibility Tree）中的节点，用于构建结构化或扁平化的页面元素快照
    /// </summary>
    public class ArianNode
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("state")]
        public string State { get; set; } = "enabled";

        [JsonPropertyName("value")]
        public string? Value { get; set; }

        [JsonPropertyName("children")]
        public List<ArianNode>? Children { get; set; }

        /// <summary>
        /// 将当前节点转换为 YAML 格式字符串（不包含子节点）
        /// </summary>
        /// <returns>当前节点的 YAML 字符串表示</returns>
        public string ToYamlString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"- role: {Role}");
            sb.AppendLine($"  name: \"{EscapeYaml(Name)}\"");
            sb.AppendLine($"  state: {State}");
            sb.AppendLine($"  value: {(Value != null ? $"\"{EscapeYaml(Value)}\"" : "null")}");
            return sb.ToString();
        }

        /// <summary>
        /// 转义 YAML 字符串中的特殊字符（如双引号、换行符）
        /// </summary>
        /// <param name="value">待转义的原始字符串</param>
        /// <returns>转义后的安全字符串</returns>
        internal static string EscapeYaml(string? value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }
    }

    /// <summary>
    /// 基于 PuppeteerSharp 提供的浏览器自动化服务，封装了页面导航、元素交互、截图、可访问性树获取等核心功能
    /// </summary>
    public class McpBrowserService : IAsyncDisposable
    {
        private readonly BrowserServiceOptions _options;
        private readonly ILogger<McpBrowserService>? _logger;
        private IBrowser? _browser;
        private IPage? _page;
        private bool _isDisposed;
        private readonly SemaphoreSlim _initializationLock = new(1, 1);

        /// <summary>
        /// 初始化 McpBrowserService 实例
        /// </summary>
        /// <param name="options">浏览器服务配置选项（通过 IOptions 注入）</param>
        /// <param name="logger">日志记录器（可选，为 null 时不记录日志）</param>
        public McpBrowserService(IOptions<BrowserServiceOptions> options, ILogger<McpBrowserService>? logger = null)
        {
            _options = options.Value;
            _logger = logger;
        }

        /// <summary>
        /// 确保浏览器和页面已初始化（线程安全，支持双重检查锁定）
        /// </summary>
        private async Task EnsureBrowserAsync()
        {
            if (_browser?.IsClosed == false && _page != null)
                return;

            await _initializationLock.WaitAsync();
            try
            {
                if (_browser?.IsClosed == false && _page != null)
                    return;

                await CleanupResourcesAsync();

                _browser = await Puppeteer.LaunchAsync(new LaunchOptions
                {
                    Headless = _options.Headless,
                    ExecutablePath = _options.ExecutablePath,
                    Args = _options.Args
                });

                _page = await _browser.NewPageAsync();
                await _page.SetViewportAsync(_options.Viewport);
                _page.DefaultTimeout = _options.DefaultTimeout;

                _logger?.LogInformation("浏览器初始化成功");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "浏览器初始化失败");
                await CleanupResourcesAsync();
                throw;
            }
            finally
            {
                _initializationLock.Release();
            }
        }

        /// <summary>
        /// 清理浏览器和页面资源（关闭页面和浏览器，释放引用）
        /// </summary>
        private async Task CleanupResourcesAsync()
        {
            if (_page != null)
            {
                try { await _page.CloseAsync(); }
                catch (Exception ex) { _logger?.LogWarning(ex, "关闭页面时出错"); }
                _page = null;
            }

            if (_browser != null && !_browser.IsClosed)
            {
                try { await _browser.CloseAsync(); }
                catch (Exception ex) { _logger?.LogWarning(ex, "关闭浏览器时出错"); }
                _browser = null;
            }
        }

        /// <summary>
        /// 获取当前页面的可访问性树（Arian 快照），支持扁平化或树形结构输出
        /// </summary>
        /// <param name="flatten">是否将树扁平化为仅包含交互式元素的列表</param>
        /// <param name="interestingOnly">是否仅包含“有意义”的节点（过滤掉 generic/none 等无意义角色）</param>
        /// <returns>YAML 格式的快照字符串，或错误信息</returns>
        public async Task<string> GetArianSnapshotAsync(bool flatten = true, bool interestingOnly = true)
        {
            try
            {
                await EnsureBrowserAsync();
                if (_page == null) throw new InvalidOperationException("页面未初始化");

                var snapshot = await _page.Accessibility.SnapshotAsync(
                    new AccessibilitySnapshotOptions { InterestingOnly = interestingOnly });

                if (snapshot == null)
                    return "# No accessibility tree available";

                var rootNode = ConvertToArianNode(snapshot);
                return flatten
                    ? ConvertListToYaml(FlattenArianTree(rootNode))
                    : ConvertTreeToYaml(rootNode, 0);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "获取Arian快照失败");
                return $"# Error getting Arian snapshot: {ex.Message}";
            }
        }

        /// <summary>
        /// 将 PuppeteerSharp 的 SerializedAXNode 转换为自定义的 ArianNode
        /// </summary>
        private ArianNode ConvertToArianNode(SerializedAXNode node)
        {
            var arianNode = new ArianNode
            {
                Role = node.Role ?? "unknown",
                Name = node.Name ?? string.Empty,
                Value = node.Value?.ToString(),
                State = DetermineState(node)
            };

            if (node.Children?.Any() == true)
            {
                arianNode.Children = node.Children
                    .Where(IsInterestingNode)
                    .Select(ConvertToArianNode)
                    .ToList();
            }

            return arianNode;
        }

        /// <summary>
        /// 根据节点属性确定状态字符串（如 disabled, checked, focused 等）
        /// </summary>
        private string DetermineState(SerializedAXNode node)
        {
            var states = new List<string> { node.Disabled ? "disabled" : "enabled" };

            if (node.Checked != CheckedState.False)
                states.Add(node.Checked == CheckedState.True ? "checked" : "mixed");
            if (node.Pressed != CheckedState.False)
                states.Add(node.Pressed == CheckedState.True ? "pressed" : "mixed");
            if (node.Focused) states.Add("focused");
            if (node.Expanded) states.Add("expanded");
            if (node.Selected) states.Add("selected");
            if (node.Required) states.Add("required");
            if (node.Readonly) states.Add("readonly");
            if (node.Multiline) states.Add("multiline");
            if (node.Modal) states.Add("modal");

            return string.Join(", ", states);
        }

        /// <summary>
        /// 判断节点是否为“有意义”的节点（过滤掉 role 为 none 或 generic 的节点）
        /// </summary>
        private bool IsInterestingNode(SerializedAXNode node)
        {
            return !string.IsNullOrEmpty(node.Role) && node.Role is not "none" and not "generic";
        }

        /// <summary>
        /// 判断角色是否为交互式角色（如 button, link, textbox 等）
        /// </summary>
        private bool IsInteractiveRole(string role) => role.ToLower() switch
        {
            "button" or "link" or "textbox" or "checkbox" or "radio"
            or "combobox" or "menuitem" or "menuitemcheckbox" or "menuitemradio"
            or "tab" or "searchbox" or "spinbutton" or "switch" or "slider"
            or "listbox" or "option" or "treeitem" => true,
            _ => false
        };

        /// <summary>
        /// 递归扁平化 Arian 树，仅保留交互式节点
        /// </summary>
        private List<ArianNode> FlattenArianTree(ArianNode node, List<ArianNode>? result = null)
        {
            result ??= new List<ArianNode>();

            if (IsInteractiveRole(node.Role))
            {
                result.Add(new ArianNode
                {
                    Role = node.Role,
                    Name = node.Name,
                    State = node.State,
                    Value = node.Value
                });
            }

            if (node.Children != null)
            {
                foreach (var child in node.Children)
                    FlattenArianTree(child, result);
            }

            return result;
        }

        /// <summary>
        /// 将扁平化的节点列表转换为 YAML 字符串
        /// </summary>
        private string ConvertListToYaml(List<ArianNode> nodes)
        {
            if (nodes.Count == 0) return "# No interactive elements found";

            var sb = new StringBuilder();
            sb.AppendLine($"# Arian Snapshot - {nodes.Count} interactive elements found");
            sb.AppendLine();

            foreach (var node in nodes)
                sb.Append(node.ToYamlString());

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// 将树形结构的节点递归转换为 YAML 字符串
        /// </summary>
        private string ConvertTreeToYaml(ArianNode node, int indent)
        {
            var sb = new StringBuilder();
            var prefix = new string(' ', indent * 2);

            sb.AppendLine($"{prefix}- role: {node.Role}");
            sb.AppendLine($"{prefix}  name: \"{ArianNode.EscapeYaml(node.Name)}\"");
            sb.AppendLine($"{prefix}  state: {node.State}");
            sb.AppendLine($"{prefix}  value: {(node.Value != null ? $"\"{ArianNode.EscapeYaml(node.Value)}\"" : "null")}");

            if (node.Children?.Any() == true)
            {
                sb.AppendLine($"{prefix}  children:");
                foreach (var child in node.Children)
                    sb.Append(ConvertTreeToYaml(child, indent + 2));
            }

            return sb.ToString();
        }

        /// <summary>
        /// 点击指定角色和名称的元素
        /// 对于 <a> 标签且带 href 的元素，直接导航；对于其他元素，通过 JS 模拟完整点击事件流
        /// </summary>
        /// <param name="role">元素角色（如 button, link）</param>
        /// <param name="name">元素名称（可访问性名称，支持模糊匹配）</param>
        /// <param name="timeoutMs">查找元素的超时时间（单位：毫秒）</param>
        /// <returns>是否成功点击</returns>
        public async Task<bool> ClickAsync(string role, string name, int timeoutMs = 8000)
        {
            try
            {
                await EnsureBrowserAsync();
                if (_page == null) throw new InvalidOperationException("页面未初始化");

                var element = await FindElementByArianAsync(role, name, timeoutMs);
                if (element == null)
                {
                    _logger?.LogWarning($"未找到元素: role={role}, name=\"{name}\"");
                    return false;
                }

                await using var elementHandle = element;

                // 获取元素标签信息，判断是否为 <a> 标签
                var elementInfo = await elementHandle.EvaluateFunctionAsync<Dictionary<string, object>>(@"el => {
                    return {
                        tagName: el.tagName?.toLowerCase() || '',
                        href: el.href || '',
                        target: el.getAttribute('target') || '',
                        hasOnClick: !!el.onclick || el.getAttribute('onclick')
                    };
                }");

                string tagName = elementInfo["tagName"]?.ToString() ?? "";
                string href = elementInfo["href"]?.ToString() ?? "";
                bool isAnchor = tagName == "a" && !string.IsNullOrEmpty(href) && href.StartsWith("http");

                // 如果是 <a> 标签，优先直接导航（更稳定）
                if (isAnchor)
                {
                    // 滚动到可见区域
                    await elementHandle.EvaluateFunctionAsync(@"el => {
                        el.scrollIntoView({ behavior: 'instant', block: 'center' });
                    }");
                    await Task.Delay(100);

                    try
                    {
                        // 直接导航，等待 DOMContentLoaded 即可
                        await _page.GoToAsync(href, new NavigationOptions
                        {
                            WaitUntil = new[] { WaitUntilNavigation.DOMContentLoaded },
                            Timeout = 5000
                        });
                        return true;
                    }
                    catch (Exception navEx)
                    {
                        _logger?.LogWarning(navEx, "导航超时，但页面可能已在加载");
                        return true; // 即使超时也认为成功
                    }
                }

                // 非 <a> 标签，走 JS 点击流程
                // 1. 滚动到视口中央并确保可点击
                await elementHandle.EvaluateFunctionAsync(@"el => {
                    el.scrollIntoView({ behavior: 'instant', block: 'center', inline: 'center' });
                    el.style.zIndex = '999999';
                    el.style.pointerEvents = 'auto';
                }");
                await Task.Delay(200);

                // 2. 触发完整的鼠标事件流
                try
                {
                    await elementHandle.EvaluateFunctionAsync(@"el => {
                        const rect = el.getBoundingClientRect();
                        const x = rect.left + rect.width / 2;
                        const y = rect.top + rect.height / 2;
                        
                        el.dispatchEvent(new MouseEvent('mouseenter', { bubbles: true, clientX: x, clientY: y }));
                        el.dispatchEvent(new MouseEvent('mousemove', { bubbles: true, clientX: x, clientY: y }));
                        el.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, clientX: x, clientY: y }));
                        el.dispatchEvent(new MouseEvent('mouseup', { bubbles: true, clientX: x, clientY: y }));
                        el.dispatchEvent(new MouseEvent('click', { bubbles: true, clientX: x, clientY: y }));
                        
                        try { el.click(); } catch(e) {}
                    }");

                    await Task.Delay(500);
                    return true;
                }
                catch (Exception jsEx)
                {
                    _logger?.LogError(jsEx, "JS 点击失败");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"点击异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 在指定元素上按下回车键
        /// </summary>
        /// <param name="role">元素角色</param>
        /// <param name="name">元素名称</param>
        /// <param name="timeoutMs">查找元素的超时时间（单位：毫秒）</param>
        /// <returns>是否成功按下回车</returns>
        public async Task<bool> PressEnter(string role, string name, int timeoutMs = 5000)
        {
            try
            {
                await EnsureBrowserAsync();
                if (_page == null) throw new InvalidOperationException("页面未初始化");

                await using var searchBox = await FindElementByArianAsync(role, name, timeoutMs);
                if (searchBox != null)
                {
                    await searchBox.FocusAsync();
                    await Task.Delay(100);
                    await searchBox.PressAsync("Enter");
                    return true;
                }
                else
                {
                    _logger?.LogWarning("未找到元素: role={Role}, name={Name}", role, name);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "按下Enter异常: role={Role}, name={Name}", role, name);
                return false;
            }
        }

        /// <summary>
        /// 在指定输入框中输入文本
        /// </summary>
        /// <param name="role">元素角色（通常为 textbox 或 searchbox）</param>
        /// <param name="name">元素名称</param>
        /// <param name="text">要输入的文本内容</param>
        /// <param name="clearFirst">输入前是否清空输入框</param>
        /// <param name="timeoutMs">查找元素的超时时间（单位：毫秒）</param>
        /// <returns>是否输入成功</returns>
        public async Task<bool> TypeAsync(string role, string name, string text, bool clearFirst = true, int timeoutMs = 5000)
        {
            try
            {
                await EnsureBrowserAsync();
                if (_page == null) throw new InvalidOperationException("页面未初始化");

                await using var element = await FindElementByArianAsync(role, name, timeoutMs);
                if (element == null)
                {
                    _logger?.LogWarning("未找到输入框: role={Role}, name={Name}", role, name);
                    return false;
                }

                // 校验元素可见且可用
                var isVisible = await element.IsVisibleAsync();
                var isEnabled = await element.EvaluateFunctionAsync<bool>("el => !el.disabled");
                if (!isVisible || !isEnabled)
                {
                    _logger?.LogWarning("输入框不可见或不可用: role={Role}, name={Name}", role, name);
                    return false;
                }

                // 聚焦输入框
                await element.FocusAsync();
                await Task.Delay(50);

                // 清空输入框（三重容错：JS清空 + 全选删除 + 按键删除）
                if (clearFirst)
                {
                    // 方案1：JS直接清空
                    await element.EvaluateFunctionAsync(@"el => {
                        el.value = '';
                        el.dispatchEvent(new Event('input', { bubbles: true }));
                        el.dispatchEvent(new Event('change', { bubbles: true }));
                    }");
                    await Task.Delay(50);

                    // 方案2：按键兜底
                    await element.ClickAsync(new ClickOptions { Count = 3, Delay = 50 });
                    await Task.Delay(50);
                    await _page.Keyboard.DownAsync("Control");
                    await element.PressAsync("a");
                    await _page.Keyboard.UpAsync("Control");
                    await Task.Delay(50);
                    await element.PressAsync("Backspace");
                    await Task.Delay(50);
                }

                // 模拟人工输入，带随机延迟
                await element.TypeAsync(text, new TypeOptions { Delay = new Random().Next(5, 20) });
                await Task.Delay(100);

                // 校验输入结果
                var inputValue = await element.EvaluateFunctionAsync<string>("el => el.value");
                if (inputValue != text)
                {
                    _logger?.LogWarning("输入结果校验失败，预期：{Expected}，实际：{Actual}", text, inputValue);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "输入失败: role={Role}, name={Name}", role, name);
                return false;
            }
        }

        /// <summary>
        /// 通过角色和名称查找元素（带重试机制）
        /// </summary>
        private async Task<IElementHandle?> FindElementByArianAsync(string role, string name, int timeoutMs)
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            var retryDelay = 100;

            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    IElementHandle? element = role.ToLower() switch
                    {
                        "link" => await FindLinkByNameAsync(name),
                        "button" => await FindButtonByNameAsync(name),
                        "textbox" or "searchbox" => await FindInputByNameAsync(name, role),
                        "tab" => await FindTabByNameAsync(name),
                        _ => await FindGenericElementByNameAsync(role, name)
                    };

                    if (element != null)
                    {
                        // 校验元素可见且可用
                        var isVisible = await element.IsVisibleAsync();
                        var isEnabled = await element.EvaluateFunctionAsync<bool>(
                            "el => !el.disabled && el.getAttribute('aria-disabled') !== 'true'");

                        if (isVisible && isEnabled)
                        {
                            return element;
                        }

                        await element.DisposeAsync();
                    }

                    await Task.Delay(retryDelay, cts.Token);
                }
                catch (OperationCanceledException) { return null; }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "查找元素重试中...");
                    await Task.Delay(retryDelay, cts.Token);
                }
            }

            _logger?.LogWarning("查找元素超时: role={Role}, name={Name}", role, name);
            return null;
        }

        /// <summary>
        /// 查找 Tab 标签元素（优先通过 role='tab' 查找，兜底 XPath）
        /// </summary>
        private async Task<IElementHandle?> FindTabByNameAsync(string name)
        {
            if (_page == null) return null;

            var targetName = name.ToLower().Trim();
            var tabElements = await _page.QuerySelectorAllAsync("[role='tab'], a[href], li[role='tab'], div[role='tab']");

            foreach (var tab in tabElements)
            {
                try
                {
                    var tabMatchText = await tab.EvaluateFunctionAsync<string>(@"el => {
                        const textContent = (el.textContent || '').trim().toLowerCase();
                        const ariaLabel = el.getAttribute('aria-label') || '';
                        const title = el.getAttribute('title') || '';
                        return (textContent + ' ' + ariaLabel + ' ' + title).toLowerCase();
                    }");

                    if (tabMatchText.Contains(targetName))
                    {
                        // 释放其他未匹配元素
                        foreach (var otherTab in tabElements.Where(t => t != tab))
                            await otherTab.DisposeAsync();

                        return tab;
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "解析Tab元素属性失败，跳过当前元素");
                }
                await tab.DisposeAsync();
            }

            // 兜底方案：XPath 查找
            try
            {
                var xpathResults = await _page.XPathAsync(
                    $"//*[contains(translate(text(), 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), '{targetName}') and (@role='tab' or name()='a')]"
                );
                if (xpathResults.Length > 0)
                {
                    return xpathResults[0];
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "XPath查找Tab元素失败");
            }

            return null;
        }

        /// <summary>
        /// 查找链接元素（<a> 标签）
        /// </summary>
        private async Task<IElementHandle?> FindLinkByNameAsync(string name)
        {
            if (_page == null) return null;

            var links = await _page.QuerySelectorAllAsync("a");
            foreach (var link in links)
            {
                try
                {
                    var linkText = await link.EvaluateFunctionAsync<string>(@"el => {
                        const text = (el.textContent || '').trim();
                        const ariaLabel = el.getAttribute('aria-label') || '';
                        const title = el.getAttribute('title') || '';
                        return (text + ' ' + ariaLabel + ' ' + title).toLowerCase();
                    }");

                    if (linkText.Contains(name.ToLower()))
                    {
                        foreach (var otherLink in links.Where(l => l != link))
                            await otherLink.DisposeAsync();
                        return link;
                    }
                }
                catch { }
                await link.DisposeAsync();
            }

            // 兜底 XPath
            try
            {
                var xpathResults = await _page.XPathAsync(
                    $"//a[contains(translate(text(), 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), '{name.ToLower()}')]"
                );
                if (xpathResults.Length > 0) return xpathResults[0];
            }
            catch { }

            return null;
        }

        /// <summary>
        /// 查找按钮元素（button, [role='button'], input[type='button/submit']）
        /// </summary>
        private async Task<IElementHandle?> FindButtonByNameAsync(string name)
        {
            if (_page == null) return null;

            var buttons = await _page.QuerySelectorAllAsync("button, [role='button'], input[type='button'], input[type='submit']");
            foreach (var button in buttons)
            {
                try
                {
                    var buttonText = await button.EvaluateFunctionAsync<string>(@"el => {
                        const text = (el.textContent || el.value || '').trim();
                        const ariaLabel = el.getAttribute('aria-label') || '';
                        const title = el.getAttribute('title') || '';
                        return (text + ' ' + ariaLabel + ' ' + title).toLowerCase();
                    }");

                    if (buttonText.Contains(name.ToLower()))
                    {
                        foreach (var otherButton in buttons.Where(b => b != button))
                            await otherButton.DisposeAsync();
                        return button;
                    }
                }
                catch { }
                await button.DisposeAsync();
            }

            return null;
        }

        /// <summary>
        /// 查找输入框元素（textbox/searchbox）
        /// </summary>
        private async Task<IElementHandle?> FindInputByNameAsync(string name, string role)
        {
            if (_page == null) return null;
            string selector = role.ToLower() switch
            {
                "searchbox" => "input[type='search'], [role='searchbox'], input[type='text'], textarea",
                _ => "input[type='text'], [role='textbox'], textarea, input:not([type])"
            };

            var inputs = await _page.QuerySelectorAllAsync(selector);
            var targetName = name.ToLower().Trim();

            foreach (var input in inputs)
            {
                try
                {
                    var elementInfo = await input.EvaluateFunctionAsync<Dictionary<string, string>>(@"el => {
                        const ariaLabel = el.getAttribute('aria-label') || '';
                        const placeholder = el.getAttribute('placeholder') || '';
                        const title = el.getAttribute('title') || '';
                        
                        let labeledByText = '';
                        const labeledById = el.getAttribute('aria-labelledby');
                        if (labeledById) {
                            const labeledEl = document.getElementById(labeledById);
                            if (labeledEl) {
                                labeledByText = labeledEl.getAttribute('aria-label') || labeledEl.textContent || '';
                            }
                        }
                        
                        return {
                            fullText: (ariaLabel + ' ' + placeholder + ' ' + title + ' ' + labeledByText).toLowerCase()
                        };
                    }");

                    if (elementInfo["fullText"].Contains(targetName))
                    {
                        foreach (var other in inputs.Where(i => i != input))
                            await other.DisposeAsync();
                        return input;
                    }
                }
                catch { }
                await input.DisposeAsync();
            }

            return null;
        }

        /// <summary>
        /// 通用元素查找（通过 [role='xxx'] 选择器）
        /// </summary>
        private async Task<IElementHandle?> FindGenericElementByNameAsync(string role, string name)
        {
            if (_page == null) return null;

            var elements = await _page.QuerySelectorAllAsync($"[role='{role}']");
            foreach (var element in elements)
            {
                try
                {
                    var elementText = await element.EvaluateFunctionAsync<string>(@"el => {
                        return (el.textContent || el.getAttribute('aria-label') || '').trim().toLowerCase();
                    }");

                    if (elementText.Contains(name.ToLower()))
                    {
                        foreach (var otherElement in elements.Where(e => e != element))
                            await otherElement.DisposeAsync();
                        return element;
                    }
                }
                catch { }
                await element.DisposeAsync();
            }

            return null;
        }

        /// <summary>
        /// 截取当前页面截图并返回 Base64 字符串
        /// </summary>
        /// <param name="fullPage">是否截取整个页面（滚动截图）</param>
        /// <returns>Base64 格式的图片字符串，或错误信息</returns>
        public async Task<string> ScreenshotAsync(bool fullPage = true)
        {
            try
            {
                await EnsureBrowserAsync();
                if (_page == null) throw new InvalidOperationException("页面未初始化");

                var imageBytes = await _page.ScreenshotDataAsync(new ScreenshotOptions
                {
                    Type = ScreenshotType.Jpeg,
                    Quality = 85,
                    FromSurface = true,
                    FullPage = fullPage
                });

                return ImageBytesToBase64(imageBytes, "jpeg");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "截图失败");
                return $"Error: {ex.Message}";
            }
        }

        /// <summary>
        /// 将图片字节数组转换为 Data URL 格式的 Base64 字符串
        /// </summary>
        /// <param name="imageBytes">图片字节数组</param>
        /// <param name="mimeType">图片 MIME 类型（如 jpeg, png）</param>
        /// <returns>Data URL 字符串</returns>
        public static string ImageBytesToBase64(byte[] imageBytes, string mimeType)
        {
            if (imageBytes == null || imageBytes.Length == 0)
                return string.Empty;

            return $"data:image/{mimeType.ToLower()};base64,{Convert.ToBase64String(imageBytes)}";
        }

        /// <summary>
        /// 导航到指定 URL
        /// </summary>
        /// <param name="url">目标 URL</param>
        /// <param name="waitUntil">等待条件数组（默认为 Networkidle2）</param>
        /// <returns>是否导航成功（HTTP 状态码 2xx）</returns>
        public async Task<bool> NavigateAsync(string url, WaitUntilNavigation[]? waitUntil = null)
        {
            try
            {
                await EnsureBrowserAsync();
                if (_page == null) throw new InvalidOperationException("页面未初始化");

                waitUntil ??= new[] { WaitUntilNavigation.Networkidle2 };
                var response = await _page.GoToAsync(url, new NavigationOptions
                {
                    WaitUntil = waitUntil,
                    Timeout = _options.DefaultTimeout
                });

                return response?.Ok ?? false;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "导航失败: {Url}", url);
                return false;
            }
        }

        /// <summary>
        /// 获取当前页面的 URL
        /// </summary>
        /// <returns>当前页面的完整 URL 字符串</returns>
        public async Task<string> GetCurrentUrlAsync()
        {
            if (_page == null) return string.Empty;
            return await _page.EvaluateExpressionAsync<string>("window.location.href");
        }

        /// <summary>
        /// 获取当前页面的标题
        /// </summary>
        /// <returns>页面标题字符串</returns>
        public async Task<string> GetTitleAsync()
        {
            if (_page == null) return string.Empty;
            return await _page.GetTitleAsync();
        }

        /// <summary>
        /// 等待指定时间（封装 Task.Delay）
        /// </summary>
        /// <param name="milliseconds">等待时间（单位：毫秒）</param>
        public async Task WaitAsync(int milliseconds) => await Task.Delay(milliseconds);

        /// <summary>
        /// 关闭浏览器并清理资源
        /// </summary>
        public async Task CloseAsync()
        {
            if (_isDisposed) return;
            await CleanupResourcesAsync();
        }

        /// <summary>
        /// 释放异步资源（实现 IAsyncDisposable）
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (_isDisposed) return;

            await CloseAsync();
            _initializationLock.Dispose();
            _isDisposed = true;
            GC.SuppressFinalize(this);
        }
    }
}