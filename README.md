# gxVoiceIn

一个 Windows 11 常驻语音输入工具。

核心体验是：

- 按住 `Right Shift` 开始录音
- 松开后自动识别并注入当前输入框
- 托盘常驻，双击可打开控制台
- 支持火山引擎 / 豆包流式 ASR
- 支持可选的大模型文本修正

## 当前功能

- 托盘常驻与托盘菜单
- `Right Shift` 热键录音
- 麦克风音量波形 HUD
- 云端流式语音识别
- 剪贴板注入文本
- ASR 设置窗口
- LLM 设置窗口
- 控制台窗口
- 基本日志与错误提示

## 技术栈

- C# / .NET 8
- WPF
- NAudio
- WebSocket

## 项目结构

```text
VoiceInputApp/
├── Controls/
├── Models/
├── Services/
├── ViewModels/
├── Windows/
├── App.xaml
└── VoiceInputApp.csproj
```

根目录还有几份简短文档：

- [PROJECT_BRIEF.md](./PROJECT_BRIEF.md)
- [ARCHITECTURE.md](./ARCHITECTURE.md)
- [TASKS.md](./TASKS.md)

## 运行方式

1. 安装 .NET 8 SDK
2. 进入项目目录
3. 构建并运行：

```powershell
dotnet build VoiceInputApp\VoiceInputApp.csproj
dotnet run --project VoiceInputApp\VoiceInputApp.csproj
```

## 配置说明

程序运行后会把本地配置写到当前用户的 AppData 目录，不在仓库里：

- ASR `AppId / Token`
- LLM `Base URL / API Key / Model`
- 当前语言和开关状态

这些本地配置和密钥不会随仓库提交。

## 当前定位

这个项目当前优先级是：

- 实时输入体验
- 连续输入的稳定性
- 热键兼容性
- 文本注入可靠性

LLM 修正能力保留为可选增强，不作为默认实时主链路。
