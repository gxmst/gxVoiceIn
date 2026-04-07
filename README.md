# gxVoiceIn

一个偏日用方向的 Windows 语音输入工具。

核心体验是：

- 按住 `Right Shift` 开始录音
- 松开后自动结束识别并注入当前输入框
- 托盘常驻，双击可打开控制台
- 优先保证实时性和稳定性

## 当前能力

- 托盘常驻与托盘右键菜单
- `Right Shift` 按住说话
- HUD 底部状态提示与音量波形
- 火山引擎大模型流式 ASR
- 剪贴板注入与恢复
- 控制台窗口
- 开机自启开关
- ASR / LLM 设置窗口
- 最近日志查看
- 可选 LLM 文本修正
- 热词表 ID 配置入口

## 识别链路

当前默认走这条主链路：

1. 按下 `Right Shift`
2. 建立 ASR 连接并开始录音
3. 松开后额外保留约 `200ms` 尾音
4. 等待最终识别结果
5. 直接注入当前输入框

为了更偏输入法体验，目前还做了这些优化：

- 默认开启 ITN、标点和顺滑参数
- 会带上当前语言
- 会携带最近几条成功上屏文本作为轻量上下文
- 支持在火山后台自定义热词表，再把 `Boosting Table ID` 填回程序

## 系统要求

- 推荐：Windows 11
- 支持目标：Windows 10 / 11
- 项目当前构建目标：`.NET 8` + `WPF`

不考虑 Windows XP / Windows 7 兼容。

## 运行方式

```powershell
dotnet build VoiceInputApp\VoiceInputApp.csproj
dotnet run --project VoiceInputApp\VoiceInputApp.csproj
```

当前有效调试输出目录：

```text
VoiceInputApp\bin\Debug\net8.0-windows10.0.19041.0
```

## 配置与本地数据

本地配置写在当前用户的 AppData 目录，不进仓库：

- ASR `AppId / Token / ResourceId / ModelName / BoostingTableId`
- LLM `Base URL / API Key / Model`
- 当前语言
- 麦克风设备
- 开关状态

日志默认写在：

```text
%APPDATA%\VoiceInputApp\logs
```

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

根目录文档：

- [PROJECT_BRIEF.md](./docs/PROJECT_BRIEF.md)
- [ARCHITECTURE.md](./docs/ARCHITECTURE.md)
- [TASKS.md](./docs/TASKS.md)
- [HOTWORDS.md](./docs/HOTWORDS.md)

## 当前阶段

第一阶段重点已经完成到“可日用”状态，当前更适合做小步优化，而不是大拆重写。

现阶段优先级：

- 实时输入体验
- 连续输入稳定性
- 热键兼容性
- 注入可靠性
- 识别准确率微调

LLM 修正保留为可选增强，不作为默认实时主链路。
