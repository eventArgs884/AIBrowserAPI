# BrowserAPI 🌐

一个基于 .NET 8 和 PuppeteerSharp 的浏览器自动化 API 服务，提供通过 HTTP 接口控制浏览器的能力，并集成了AI对话功能。

## ✨ 特性

- **🎯 可访问性树快照**：获取页面的 Accessibility Tree 结构，支持扁平化或树形输出
- **🖱️ 智能元素交互**：基于角色和名称的点击、输入、回车操作
- **📸 页面截图**：支持视口或全页截图，返回 Base64 编码图片
- **🔗 导航控制**：以编程方式控制浏览器导航到指定 URL
- **🌐 页面信息**：获取当前页面的 URL 和标题
- **⏱️ 等待控制**：支持自定义等待时间，用于处理页面加载和动画
- **🤖 AI 对话集成**：支持与本地AI对话，包含工具调用功能
- **📝 历史记录管理**：保存和恢复对话历史
- **⚙️ 灵活配置**：通过配置文件自定义浏览器行为（无头模式、超时时间、视口尺寸等）

## 📋 系统要求

- **.NET 8.0 SDK** 或更高版本
- **Google Chrome** 或 Chromium 浏览器（需预先安装）
- **LM Studio**（可选，用于本地AI对话）
- Windows / Linux / macOS（根据实际部署环境调整）

## 🚀 快速开始

### 1️⃣ 克隆项目

```bash
git clone https://gitee.com/eventargs/AIBrowserAPI.git
或者
git clone https://github.com/eventArgs884/AIBrowserAPI.git
```

### 2️⃣ 使用 Visual Studio 打开项目

1. 双击 `BrowserAPI.sln` 文件直接打开项目
2. 在 Visual Studio 中，右键点击项目并选择 "设为启动项目"
3. 按 `F5` 启动调试，或 `Ctrl+F5` 启动而不调试

### 3️⃣ 配置浏览器路径

编辑 [`BrowserAPI/appsettings.json`](BrowserAPI/appsettings.json) 文件，修改 `ExecutablePath` 为你的 Chrome 安装路径：

```json
{
  "BrowserSettings": {
    "Headless": false,
    "ExecutablePath": "D:\\chrome-win64\\chrome.exe",
    ...
  }
}
```

**常见路径参考：**

| 操作系统           | 默认路径                                                           |
| -------------- | -------------------------------------------------------------- |
| Windows (64 位) | `C:\Program Files\Google\Chrome\Application\chrome.exe`        |
| Windows (便携版)  | `D:\\chrome-win64\\chrome.exe`（示例）                             |
| Linux          | `/usr/bin/google-chrome`                                       |
| macOS          | `/Applications/Google Chrome.app/Contents/MacOS/Google Chrome` |

### 4️⃣ 配置AI（可选）

如果要使用AI对话功能，请确保LM Studio正在运行，并配置以下内容：

```json
{
  "AI": {
    "BaseUrl": "http://localhost:11434",
    "Model": "qwen/qwen3.5-35b-a3b"
  }
}
```

### 5️⃣ 启动服务

```bash
dotnet run
```

服务将在 `http://localhost:5096`（或配置端口）上运行。

## 📖 API 文档

### 基础信息

| 项目           | 值                                   |
| ------------ | ----------------------------------- |
| **Base URL** | `http://localhost:5096/api` |
| **认证**       | 无需认证（本地开发环境）                        |

***

## 🤖 聊天API

### 1️⃣ 发送消息给AI 💬

发送消息给AI并获取响应，支持流式和非流式两种模式。

#### 流式请求（推荐）

```http
POST /api/chat/send
Content-Type: application/json

{
  "message": "你好，请帮我导航到百度",
  "stream": true,
  "loadHistory": true
}
```

**参数说明：**

| 字段             | 类型      | 默认值     | 说明                     |
| -------------- | ------- | ------- | ---------------------- |
| `message`      | string  | -       | 用户发送的消息                |
| `stream`       | boolean | `true`  | 是否使用流式响应               |
| `loadHistory`  | boolean | `true`  | 是否加载历史记录               |

**SSE 事件类型：**

| 事件类型           | 说明                                 |
| -------------- | ---------------------------------- |
| `content`      | AI生成的文本内容                          |
| `tool_calls`   | AI请求的工具调用                         |
| `tool_result`  | 工具执行结果                             |
| `done`         | 对话完成                               |

#### 非流式请求

```http
POST /api/chat/send
Content-Type: application/json

{
  "message": "你好",
  "stream": false
}
```

**响应示例：**

```json
{
  "success": true,
  "content": "你好！有什么我可以帮助你的吗？"
}
```

***

### 2️⃣ 获取可用工具列表 🛠️

获取所有可用的AI工具列表。

```http
GET /api/chat/tools
```

**响应示例：**

```json
{
  "success": true,
  "tools": [
    {
      "type": "function",
      "function": {
        "name": "navigate_to_url",
        "description": "导航到指定的URL",
        "parameters": {
          "type": "object",
          "properties": {
            "url": {
              "type": "string",
              "description": "要导航到的URL"
            }
          },
          "required": ["url"],
          "additionalProperties": false
        }
      }
    }
  ]
}
```

***

### 3️⃣ 保存历史记录 💾

保存当前对话历史到本地JSON文件。

```http
POST /api/chat/history/save
Content-Type: application/json

{
  "model": "qwen/qwen3.5-35b-a3b",
  "messages": [...],
  "stream": true
}
```

**响应示例：**

```json
{
  "success": true,
  "message": "历史记录保存成功"
}
```

***

### 4️⃣ 加载历史记录 📂

从本地JSON文件加载对话历史。

```http
GET /api/chat/history/load
```

**响应示例：**

```json
{
  "success": true,
  "data": {
    "createdAt": "2024-01-01T00:00:00",
    "updatedAt": "2024-01-01T00:00:00",
    "conversation": {
      "model": "qwen/qwen3.5-35b-a3b",
      "messages": [...],
      "stream": true
    }
  }
}
```

***

### 5️⃣ 清除历史记录 🗑️

删除本地历史记录文件。

```http
DELETE /api/chat/history/clear
```

**响应示例：**

```json
{
  "success": true,
  "message": "历史记录已清除"
}
```

***

## 🌐 浏览器API

### 1️⃣ 获取 Arian 快照 📋

获取当前页面的可访问性树结构，可用于分析页面元素或进行自动化测试。

#### 扁平化输出（推荐用于脚本处理）

```http
GET /api/browser/Arian?flatten=true&interestingOnly=true
```

**参数说明：**

| 参数                | 类型      | 默认值    | 说明                   |
| ----------------- | ------- | ------ | -------------------- |
| `flatten`         | boolean | `true` | 是否扁平化为元素列表           |
| `interestingOnly` | boolean | `true` | 是否仅包含交互式元素（过滤掉无意义节点） |

**响应示例（YAML 格式）：**

```yaml
# Arian Snapshot - 5 interactive elements found

- role: heading
  name: "Welcome to Example"
  state: enabled
  value: null

- role: textbox
  name: "Search"
  state: enabled
  value: null

- role: button
  name: "Submit"
  state: enabled
  value: null

- role: link
  name: "Login"
  state: enabled
  value: null

- role: checkbox
  name: "Remember me"
  state: enabled
  value: null
```

#### 树形输出（保留层级结构）

```http
GET /api/browser/Arian?flatten=false
```

**响应示例：**

```yaml
- role: document
  name: ""
  state: enabled
  value: null
  children:
    - role: heading
      name: "Welcome"
      state: enabled
      value: null
    - role: textbox
      name: "Search"
      state: enabled
      value: null
```

***

### 2️⃣ 导航到 URL 🚀

控制浏览器导航到指定网页。

```http
POST /api/browser/Navigate
Content-Type: application/json

{
  "url": "https://www.example.com",
  "waitUntil": ["networkidle2"]
}
```

**参数说明：**

| 字段          | 类型     | 默认值                | 说明                                                                    |
| ----------- | ------ | ------------------ | --------------------------------------------------------------------- |
| `url`       | string | -                  | 目标 URL                                                                |
| `waitUntil` | array  | `["networkidle2"]` | 等待条件数组，可选值：`load`, `domcontentloaded`, `networkidle0`, `networkidle2` |

**响应：**

| HTTP 状态码          | 说明                    |
| ----------------- | --------------------- |
| `200 OK`          | 导航成功                  |
| `400 Bad Request` | 导航失败（如 URL 无效、页面加载超时） |

***

### 3️⃣ 点击元素 🖱️

通过角色和名称点击页面上的元素。

```http
POST /api/browser/Click
Content-Type: application/json

{
  "role": "button",
  "name": "Submit",
  "timeoutMs": 8000
}
```

**参数说明：**

| 字段          | 类型      | 默认值    | 说明                                  |
| ----------- | ------- | ------ | ----------------------------------- |
| `role`      | string  | -      | 元素角色（如 `button`, `link`, `textbox`） |
| `name`      | string  | -      | 元素的名称/标签文本                          |
| `timeoutMs` | integer | `8000` | 查找元素的超时时间（毫秒）                       |

**支持的角色类型：**

- `button` - 按钮
- `link` - 链接
- `textbox` / `searchbox` - 输入框
- `checkbox` - 复选框
- `radio` - 单选框
- `combobox` - 组合框
- `tab` - 标签页

**响应：**

| HTTP 状态码          | 说明         |
| ----------------- | ---------- |
| `200 OK`          | 点击成功       |
| `400 Bad Request` | 未找到元素或点击失败 |

***

### 4️⃣ 输入文本 📝

在指定的输入框中输入文本内容。

```http
POST /api/browser/Type
Content-Type: application/json

{
  "role": "textbox",
  "name": "Search",
  "text": "Hello World",
  "clearFirst": true,
  "timeoutMs": 5000
}
```

**参数说明：**

| 字段           | 类型      | 默认值    | 说明                                 |
| ------------ | ------- | ------ | ---------------------------------- |
| `role`       | string  | -      | 输入框角色（通常为 `textbox` 或 `searchbox`） |
| `name`       | string  | -      | 输入框的名称/标签                          |
| `text`       | string  | -      | 要输入的文本内容                           |
| `clearFirst` | boolean | `true` | 输入前是否清空现有内容                        |
| `timeoutMs`  | integer | `5000` | 查找元素的超时时间（毫秒）                      |

**响应：**

| HTTP 状态码          | 说明         |
| ----------------- | ---------- |
| `200 OK`          | 输入成功       |
| `400 Bad Request` | 未找到元素或输入失败 |

***

### 5️⃣ 按下回车键 ⏎

在指定的输入框中按下 Enter 键。

```http
POST /api/browser/PressEnter
Content-Type: application/json

{
  "role": "searchbox",
  "name": "Search",
  "timeoutMs": 5000
}
```

**响应：**

| HTTP 状态码          | 说明         |
| ----------------- | ---------- |
| `200 OK`          | 按下成功       |
| `400 Bad Request` | 未找到元素或操作失败 |

***

### 6️⃣ 截图 📸

截取当前页面的截图并返回 Base64 编码的图片。

```http
GET /api/browser/Screenshot?fullPage=true
```

**参数说明：**

| 参数         | 类型      | 默认值    | 说明               |
| ---------- | ------- | ------ | ---------------- |
| `fullPage` | boolean | `true` | 是否截取整个页面（包括滚动区域） |

**响应示例：**

```json
{
  "Base64Image": "data:image/jpeg;base64,/9j/4AAQSkZJRg..."
}
```

***

### 7️⃣ 获取当前页面 URL 🌐

获取浏览器当前打开页面的完整 URL。

```http
GET /api/browser/CurrentUrl
```

**响应示例：**

```json
{
  "Url": "https://www.example.com"
}
```

***

### 8️⃣ 获取页面标题 📄

获取当前页面的标题。

```http
GET /api/browser/Title
```

**响应示例：**

```json
{
  "Title": "Example Domain"
}
```

***

### 9️⃣ 等待指定时间 ⏱️

让浏览器等待指定的时间（毫秒），用于处理页面加载或动画。

```http
POST /api/browser/Wait
Content-Type: application/json

{
  "milliseconds": 2000
}
```

**参数说明：**

| 字段             | 类型      | 默认值 | 说明       |
| -------------- | ------- | --- | -------- |
| `milliseconds` | integer | -   | 等待时间（毫秒） |

**响应：**

| HTTP 状态码 | 说明   |
| -------- | ---- |
| `200 OK` | 等待完成 |

***

### 🔟 关闭浏览器 🚪

关闭浏览器并清理资源。

```http
POST /api/browser/Close
```

**响应：**

| HTTP 状态码 | 说明     |
| -------- | ------ |
| `200 OK` | 浏览器已关闭 |

***

### 1️⃣1️⃣ 获取浏览器配置选项 ⚙️

获取当前浏览器服务的配置选项。

```http
GET /api/browser/Options
```

**响应示例：**

```json
{
  "Headless": false,
  "ExecutablePath": "D:\\chrome-win64\\chrome.exe",
  "Args": [
    "--no-sandbox",
    "--disable-setuid-sandbox",
    "--disable-dev-shm-usage"
  ],
  "DefaultTimeout": 30000,
  "Viewport": {
    "Width": 1920,
    "Height": 1080,
    "DeviceScaleFactor": 1,
    "IsMobile": false,
    "HasTouch": false
  }
}
```

***

## ⚙️ 配置说明

### appsettings.json 配置项

| 配置项               | 类型      | 默认值                                                                       | 说明                          |
| ----------------- | ------- | ------------------------------------------------------------------------- | --------------------------- |
| `AI.BaseUrl`      | string  | `http://localhost:11434`                                                   | AI服务器地址                     |
| `AI.Model`        | string  | `qwen/qwen3.5-35b-a3b`                                                  | AI模型名称                      |
| `Headless`        | boolean | `false`                                                                   | 是否以无头模式启动（服务器环境推荐设为 `true`） |
| `ExecutablePath`  | string  | -                                                                         | Chrome/Chromium 可执行文件路径     |
| `DefaultTimeout`  | integer | `30000`                                                                   | 页面操作默认超时时间（毫秒）              |
| `Args`            | array   | `["--no-sandbox", "--disable-setuid-sandbox", "--disable-dev-shm-usage"]` | 浏览器启动参数                     |
| `Viewport.Width`  | integer | `1920`                                                                    | 视口宽度                        |
| `Viewport.Height` | integer | `1080`                                                                    | 视口高度                        |

### 常用配置示例

**生产环境（无头模式）：**

```json
{
  "BrowserSettings": {
    "Headless": true,
    "ExecutablePath": "/usr/bin/google-chrome",
    "DefaultTimeout": 30000,
    "Viewport": {
      "Width": 1920,
      "Height": 1080
    }
  }
}
```

**移动端模拟：**

```json
{
  "BrowserSettings": {
    "Headless": true,
    "Args": [
      "--no-sandbox",
      "--disable-setuid-sandbox",
      "--disable-dev-shm-usage",
      "--window-size=375,667"
    ]
  }
}
```

## 🧪 运行测试

项目包含xUnit测试用例，运行测试：

```bash
dotnet test
```

测试覆盖：
- Speckjson模型测试
- ChatController模型测试

## 🛠️ 开发指南

### 项目结构

```
BrowserAPI/
├── BrowserAutomation.cs      # 核心浏览器自动化服务实现
├── Controllers/
│   ├── BrowserController.cs  # 浏览器RESTful API 控制器
│   └── ChatController.cs     # 聊天/AI API 控制器
├── Speckjson.cs              # AI对话模型定义
├── Program.cs                # 应用入口和依赖注入配置
├── appsettings.json          # 配置文件
├── appsettings.Development.json  # 开发环境配置
├── BrowserAPI.http           # HTTP 请求示例文件
├── launchSettings.json       # 启动设置
└── BrowserAPI.csproj         # 项目文件

BrowserAPI.Tests/             # 测试项目
├── SpeckjsonTests.cs         # Speckjson模型测试
└── ChatControllerTests.cs    # ChatController测试
```

### 核心类说明

| 类名                                                            | 说明                   |
| ------------------------------------------------------------- | -------------------- |
| [`McpBrowserService`](BrowserAPI/BrowserAutomation.cs:103)    | 浏览器自动化服务主类，封装所有浏览器操作 |
| [`BrowserServiceOptions`](BrowserAPI/BrowserAutomation.cs:14) | 浏览器配置选项类             |
| [`ArianNode`](BrowserAPI/BrowserAutomation.cs:57)             | 可访问性树节点模型            |
| [`ChatController`](BrowserAPI/Controllers/ChatController.cs)   | 聊天/AI对话控制器           |

### 添加自定义 API

在 [`BrowserController.cs`](BrowserAPI/Controllers/BrowserController.cs) 或 [`ChatController.cs`](BrowserAPI/Controllers/ChatController.cs) 中添加新的端点：

```csharp
[HttpGet("CustomEndpoint")]
public async Task<IActionResult> CustomEndpointAsync()
{
    var result = await _browserService.CustomOperationAsync();
    return Ok(result);
}
```

## 🐛 故障排除

### 常见问题

**1. "无法找到 Chrome 可执行文件"**

- 检查 `appsettings.json` 中的 `ExecutablePath` 是否正确
- Windows 用户可使用 [PuppeteerSharp](https://github.com/hardkoded/puppeteer-sharp) 自动下载 Chromium：
  ```bash
  dotnet add package Microsoft.Playwright
  ```

**2. "浏览器启动失败"**

- Linux 环境需要安装依赖：
  ```bash
  sudo apt-get install -y libnss3 libatk1.0-0 libatk-bridge2.0-0 libcups2 libdrm2 libxkbcommon0 libxcomposite1 libxdamage1 libxfixes3 libxrandr2 libgbm1 libasound2
  ```

**3. "元素查找失败"**

- 先调用 `GET /api/browser/Arian` 查看页面当前结构
- 确认角色（role）和名称（name）参数匹配

**4. "AI连接失败"**

- 确保LM Studio正在运行
- 检查 `appsettings.json` 中的 `AI.BaseUrl` 配置是否正确
- 确认AI模型已在LM Studio中加载

## 📦 依赖项

| 依赖             | 版本      | 说明                          |
| -------------- | ------- | --------------------------- |
| PuppeteerSharp | 24.40.0 | .NET 版本的 Puppeteer，用于浏览器自动化 |
| ASP.NET Core   | 8.0     | Web API 框架                  |
| xunit          | 2.6.6   | 测试框架                        |
| Moq            | 4.20.72 | Mock框架                      |

## 📄 许可证

本项目采用 MIT 许可证。详见 [LICENSE](LICENSE) 文件。

## 🤝 贡献

欢迎提交 Issue 和 Pull Request！

***

**Made with ❤️ using .NET 8 and PuppeteerSharp**
