# 语音输入法 - 核心设计文档

## 总体架构

```
┌─────────────────────────────────────────────────────────────────┐
│                         VoiceInputApp                            │
│                      (WPF Application)                           │
├─────────────────────────────────────────────────────────────────┤
│                                                                   │
│  ┌──────────────┐    ┌──────────────┐    ┌──────────────┐       │
│  │  TrayIcon    │    │  Hotkey      │    │  HUD         │       │
│  │  Service     │    │  Monitor     │    │  Overlay     │       │
│  └──────────────┘    └──────────────┘    └──────────────┘       │
│         │                   │                   │                │
│         └───────────────────┼───────────────────┘                │
│                             ▼                                    │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │                   Orchestrator                          │    │
│  │              (主流程协调器)                              │    │
│  └─────────────────────────────────────────────────────────┘    │
│                             │                                    │
│         ┌───────────────────┼───────────────────┐               │
│         ▼                   ▼                   ▼               │
│  ┌──────────────┐    ┌──────────────┐    ┌──────────────┐       │
│  │ AudioCapture │    │Transcription │    │TextInjection │       │
│  │   Service    │───▶│   Service    │───▶│   Service    │       │
│  └──────────────┘    └──────────────┘    └──────────────┘       │
│                             │                                    │
│                             ▼                                    │
│                     ┌──────────────┐                            │
│                     │ LLM Refine   │                            │
│                     │   Service    │                            │
│                     └──────────────┘                            │
│                                                                   │
├─────────────────────────────────────────────────────────────────┤
│                      Infrastructure                              │
│  ┌──────────────┐    ┌──────────────┐    ┌──────────────┐       │
│  │  Settings    │    │   Logger     │    │ Win32 Interop│       │
│  │  Service     │    │   Service    │    │   Helpers    │       │
│  └──────────────┘    └──────────────┘    └──────────────┘       │
└─────────────────────────────────────────────────────────────────┘
```

## 当前实现说明

当前代码已经完成 MVP 主链路，不再是纯设计阶段。现状如下：

- 托盘、热键、录音、ASR、HUD、文本注入、设置窗口均已接通
- 主流程已经做过一轮重构，重点收口了状态流、ASR 生命周期和注入兜底
- 当前优化重点是连续输入体验、热键兼容性、注入稳定性和 HUD 细节
- LLM 纠错保留为可选增强能力，不作为实时主链路默认步骤

## 模块划分

### 1. App / Bootstrap

**职责**：应用启动、依赖注入配置、生命周期管理

```
VoiceInputApp/
├── App.xaml              # WPF 应用入口
├── App.xaml.cs           # 启动逻辑、DI 容器配置
└── Bootstrapper.cs       # 服务注册、初始化
```

**关键类**：
- `App` - WPF 应用基类，管理启动流程
- `Bootstrapper` - 配置所有服务的依赖注入

### 2. Tray（系统托盘）

**职责**：托盘图标管理、右键菜单

```
Tray/
├── TrayIconService.cs    # 托盘图标生命周期
├── TrayMenuBuilder.cs    # 右键菜单构建
└── TrayResources.cs      # 托盘图标资源
```

**关键类**：
- `TrayIconService` - 创建/销毁托盘图标，处理菜单事件
- `TrayMenuBuilder` - 构建语言切换、LLM 设置、退出等菜单项

### 3. Hotkey / Keyboard Monitor

**职责**：全局键盘监听、Right Shift 检测

```
Hotkey/
├── IHotkeyMonitor.cs         # 接口定义
├── LowLevelKeyboardHook.cs   # Win32 键盘钩子封装
├── HotkeyMonitor.cs          # Right Shift 监听实现
└── HotkeyEventArgs.cs        # 按键事件参数
```

**关键类**：
- `IHotkeyMonitor` - 热键监听接口
- `LowLevelKeyboardHook` - P/Invoke 封装 `SetWindowsHookEx`
- `HotkeyMonitor` - 区分左/右 Shift，触发按下/松开事件

**Win32 API**：
- `SetWindowsHookEx(WH_KEYBOARD_LL, ...)`
- `CallNextHookEx`
- `UnhookWindowsHookEx`

### 4. Audio Capture

**职责**：麦克风录音、音频电平计算

```
Audio/
├── IAudioCaptureService.cs    # 接口定义
├── AudioCaptureService.cs     # NAudio 录音实现
├── AudioLevelCalculator.cs    # RMS 电平计算
└── AudioBuffer.cs             # 音频数据缓冲
```

**关键类**：
- `IAudioCaptureService` - 录音服务接口
- `AudioCaptureService` - 使用 NAudio 进行麦克风录音
- `AudioLevelCalculator` - 计算实时 RMS 电平，驱动波形动画

**依赖**：NAudio (NuGet)

### 5. Transcription（语音识别）

**职责**：语音转文字、流式识别

```
Transcription/
├── ITranscriptionService.cs       # 接口定义
├── TranscriptionResult.cs         # 识别结果模型
├── VolcengineAsrService.cs        # 火山引擎 ASR 实现
├── VolcengineAsrClient.cs         # WebSocket 客户端
└── Language.cs                    # 支持的语言枚举
```

**关键类**：
- `ITranscriptionService` - 语音识别接口，支持流式回调
- `VolcengineAsrService` - 火山引擎/豆包语音云端流式 ASR
- `TranscriptionResult` - 包含中间结果、最终结果、是否完成

**依赖**：System.Net.WebSockets

### 6. HUD Overlay

**职责**：屏幕底部胶囊悬浮窗、波形动画、文本显示

```
HUD/
├── HudWindow.xaml            # HUD 窗口 XAML
├── HudWindow.xaml.cs         # 窗口代码
├── HudViewModel.cs           # MVVM ViewModel
├── HudStateManager.cs        # 状态机管理
├── WaveformControl.xaml      # 波形控件
├── WaveformControl.xaml.cs   # 波形动画逻辑
└── HudAnimationHelper.cs     # 入场/退场动画
```

**关键类**：
- `HudWindow` - 无边框、置顶、不抢焦点的 WPF 窗口
- `HudViewModel` - 绑定文本、状态、波形电平
- `HudStateManager` - 管理 Idle/Listening/Transcribing/Refining/Success/Error 状态
- `WaveformControl` - 5 根竖条，权重 `[0.5, 0.8, 1.0, 0.75, 0.55]`，RMS 驱动

**Win32 API**：
- `SetWindowExStyle` - 设置 `WS_EX_NOACTIVATE`、`WS_EX_TOOLWINDOW`
- `SetWindowPos` - 设置 `HWND_TOPMOST`

### 7. Text Injection

**职责**：文本注入到当前输入框

```
Injection/
├── ITextInjectionService.cs      # 接口定义
├── ClipboardInjectionService.cs  # 剪贴板 + Ctrl+V 实现
├── ClipboardHelper.cs            # 剪贴板备份/恢复
└── InputSimulator.cs             # SendInput 封装
```

**关键类**：
- `ITextInjectionService` - 文本注入接口
- `ClipboardInjectionService` - 备份剪贴板 → 写入文本 → 模拟 Ctrl+V → 恢复剪贴板
- `InputSimulator` - P/Invoke 封装 `SendInput`

**Win32 API**：
- `OpenClipboard`、`EmptyClipboard`、`SetClipboardData`、`CloseClipboard`
- `SendInput` - 模拟键盘输入

### 8. LLM Refinement

**职责**：识别结果保守纠错

```
LLM/
├── ILlmRefinementService.cs      # 接口定义
├── OpenAiRefinementService.cs    # OpenAI 兼容 API 实现
├── LlmSettings.cs                # LLM 配置模型
└── RefinementPrompt.cs           # 纠错 Prompt 模板
```

**关键类**：
- `ILlmRefinementService` - LLM 纠错接口
- `OpenAiRefinementService` - 调用 `/chat/completions` API
- `LlmSettings` - Base URL、API Key、Model 配置

**依赖**：System.Net.Http.Json

### 9. Settings / Configuration

**职责**：用户配置持久化

```
Settings/
├── ISettingsService.cs       # 接口定义
├── SettingsService.cs        # JSON 文件读写
├── AppSettings.cs            # 配置模型
└── SettingsWindow.xaml       # LLM 设置窗口
```

**关键类**：
- `AppSettings` - 包含语言、LLM 配置等
- `SettingsService` - 读写 `settings.json`

**配置文件**：`%AppData%/VoiceInput/settings.json`

### 10. Logging

**职责**：统一日志输出

```
Logging/
├── ILogger.cs                # 日志接口
├── FileLogger.cs             # 文件日志
└── LoggerService.cs          # 日志服务
```

**日志文件**：`%AppData%/VoiceInput/logs/`

### 11. Notification

**职责**：托盘气泡通知

```
Notification/
├── INotificationService.cs   # 通知接口
└── TrayNotificationService.cs # 托盘气泡通知实现
```

**关键类**：
- `INotificationService` - 通知服务接口
- `TrayNotificationService` - 使用 NotifyIcon.ShowBalloonTip 显示通知

**通知类型**：
- Info - 信息提示
- Warning - 警告
- Error - 错误

### 12. Utilities / Win32 Interop

**职责**：Win32 API 封装

```
Interop/
├── User32.cs                 # 窗口相关 API
├── Kernel32.cs               # 系统相关 API
├── ClipboardNative.cs        # 剪贴板原生操作
└── KeyboardNative.cs         # 键盘原生操作
```

## 页面划分

### 1. HUD 窗口（HudWindow）

**用途**：录音时显示波形和实时文本

**特性**：
- 无边框、置顶、不抢焦点
- 圆角胶囊形状（圆角 28px）
- 半透明毛玻璃背景
- 左侧波形区域（44x32px）
- 右侧文本区域（160px ~ 560px 弹性宽度）

**状态**：
- Idle → Hidden
- Listening → 显示波形 + 实时文本
- Transcribing → 显示 "识别中..."
- Refining → 显示 "Refining..."
- Success → 短暂显示后淡出
- Error → 显示错误信息后淡出

### 2. 设置窗口（SettingsWindow）

**用途**：配置 LLM Refinement

**控件**：
- API Base URL 输入框
- API Key 输入框（PasswordBox）
- Model 输入框
- Test 按钮
- Save 按钮
- Cancel 按钮

## 数据模型

### AppSettings

```csharp
public class AppSettings
{
    public Language Language { get; set; } = Language.ZhCN;
    public bool LlmEnabled { get; set; } = false;
    public LlmSettings Llm { get; set; } = new();
    public AsrSettings Asr { get; set; } = new();
}

public class LlmSettings
{
    public string BaseUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "";
}

public class AsrSettings
{
    public string AppId { get; set; } = "";
    public string Token { get; set; } = "";
}
```

### TranscriptionResult

```csharp
public class TranscriptionResult
{
    public string Text { get; set; }
    public bool IsFinal { get; set; }
    public bool IsError { get; set; }
    public string ErrorMessage { get; set; }
}
```

### HudState

```csharp
public enum HudState
{
    Hidden,
    Listening,
    Transcribing,
    Refining,
    Success,
    Error
}
```

## 服务层设计

### 服务接口

```csharp
public interface IHotkeyMonitor
{
    event EventHandler<HotkeyEventArgs> KeyPressed;
    event EventHandler<HotkeyEventArgs> KeyReleased;
    void Start();
    void Stop();
}

public interface IAudioCaptureService
{
    event EventHandler<AudioLevelEventArgs> AudioLevelUpdated;
    event EventHandler<byte[]> AudioDataAvailable;
    void StartCapture();
    void StopCapture();
    float GetCurrentLevel();
}

public interface ITranscriptionService
{
    Task StartStreamingRecognitionAsync(
        Language language,
        Action<TranscriptionResult> onResult,
        CancellationToken cancellationToken);
    void SendAudioData(byte[] data);
    Task StopRecognitionAsync();
}

public interface ITextInjectionService
{
    Task<bool> InjectTextAsync(string text);
}

public interface ILlmRefinementService
{
    Task<string> RefineAsync(string text, Language language);
    bool IsConfigured { get; }
}

public interface ISettingsService
{
    AppSettings Load();
    void Save(AppSettings settings);
}
```

## Provider 抽象

### ITranscriptionService 抽象

语音识别服务抽象，便于后续替换：

```csharp
public interface ITranscriptionService
{
    Task StartStreamingRecognitionAsync(
        Language language,
        Action<TranscriptionResult> onResult,
        CancellationToken cancellationToken);
    void SendAudioData(byte[] data);
    Task StopRecognitionAsync();
}
```

**当前实现**：`VolcengineAsrService`（火山引擎）

**未来可扩展**：
- `WhisperLocalService` - 本地 Whisper
- `AzureSpeechService` - Azure Speech SDK
- `GoogleSpeechService` - Google Cloud Speech

### ILlmRefinementService 抽象

LLM 纠错服务抽象：

```csharp
public interface ILlmRefinementService
{
    Task<string> RefineAsync(string text, Language language);
    bool IsConfigured { get; }
}
```

**当前实现**：`OpenAiRefinementService`（OpenAI 兼容 API）

## 本地存储

### 配置文件

**路径**：`%AppData%/VoiceInput/settings.json`

```json
{
  "language": "ZhCN",
  "llmEnabled": false,
  "llm": {
    "baseUrl": "",
    "apiKey": "",
    "model": ""
  },
  "asr": {
    "appId": "",
    "token": ""
  }
}
```

### 日志文件

**路径**：`%AppData%/VoiceInput/logs/app-{date}.log`

**格式**：`[时间] [级别] [模块] 消息`

## MVVM 结构

### ViewModel 层

```
ViewModels/
├── HudViewModel.cs          # HUD 数据绑定
├── SettingsViewModel.cs     # 设置窗口数据绑定
└── TrayViewModel.cs         # 托盘菜单状态
```

### 数据绑定

**HudViewModel**：
- `HudState State` - 当前状态
- `string DisplayText` - 显示文本
- `float[] WaveformLevels` - 5 根竖条电平
- `bool IsVisible` - 是否可见

**SettingsViewModel**：
- `string BaseUrl` - API Base URL
- `string ApiKey` - API Key
- `string Model` - Model 名称
- `ICommand TestCommand` - 测试命令
- `ICommand SaveCommand` - 保存命令
- `ICommand CancelCommand` - 取消命令

## HUD 状态机

```
         ┌──────────────────────────────────────┐
         │                                      │
         ▼                                      │
    ┌─────────┐    按下 Right Shift    ┌────────┴─┐
    │ Hidden  │───────────────────────▶│Listening │
    └─────────┘                        └──────────┘
         ▲                                   │
         │                                   │ 松开 Right Shift
         │                                   ▼
         │                            ┌─────────────┐
         │                            │Transcribing │
         │                            └─────────────┘
         │                                   │
         │                    ┌──────────────┴──────────────┐
         │                    │                             │
         │                    ▼                             ▼
         │             ┌──────────┐                  ┌─────────┐
         │             │ Refining │                  │ Success │
         │             └──────────┘                  └─────────┘
         │                    │                             │
         │                    ▼                             │
         │             ┌─────────┐                          │
         └─────────────│ Success │◀─────────────────────────┘
                       └─────────┘
                             │
                             │ 1.5s 后自动隐藏
                             ▼
                       ┌─────────┐
                       │ Hidden  │
                       └─────────┘

         任何状态出错 ──────▶ Error ──▶ 2s 后隐藏 ──▶ Hidden
```

## 主流程时序

```
用户                    HotkeyMonitor       Orchestrator        AudioCapture        ASR Service         LLM Service         TextInjection        HUD
 │                           │                   │                   │                   │                   │                   │               │
 │──按住 Right Shift────────▶│                   │                   │                   │                   │                   │               │
 │                           │──KeyPressed──────▶│                   │                   │                   │                   │               │
 │                           │                   │──StartCapture────▶│                   │                   │                   │               │
 │                           │                   │                   │──AudioLevel──────▶│                   │                   │               │
 │                           │                   │──────────────────────────────────────────────────────────────────────────────────────────────▶│
 │                           │                   │                   │                   │                   │                   │           显示 HUD
 │                           │                   │                   │──AudioData───────▶│                   │                   │               │
 │                           │                   │                   │                   │──SendToASR──────▶│                   │               │
 │                           │                   │                   │                   │                   │                   │               │
 │──说话─────────────────────────────────────────────────────────────────────────────────▶│                   │                   │               │
 │                           │                   │                   │                   │──中间结果────────▶│                   │               │
 │                           │                   │◀──────────────────────────────────────│                   │                   │           更新文本
 │                           │                   │──────────────────────────────────────────────────────────────────────────────────────────────▶│
 │                           │                   │                   │                   │                   │                   │               │
 │──松开 Right Shift────────▶│                   │                   │                   │                   │                   │               │
 │                           │──KeyReleased─────▶│                   │                   │                   │                   │               │
 │                           │                   │──StopCapture─────▶│                   │                   │                   │               │
 │                           │                   │──StopRecognition────────────────────▶│                   │                   │               │
 │                           │                   │                   │                   │──最终结果────────▶│                   │               │
 │                           │                   │◀──────────────────────────────────────│                   │                   │               │
 │                           │                   │                   │                   │                   │                   │           显示 Transcribing
 │                           │                   │                   │                   │                   │                   │               │
 │                           │                   │──Refine(可选)────────────────────────────────────────────▶│                   │               │
 │                           │                   │                   │                   │                   │──纠错结果────────▶│               │
 │                           │                   │◀──────────────────────────────────────────────────────────│                   │           显示 Refining
 │                           │                   │                   │                   │                   │                   │               │
 │                           │                   │──InjectText──────────────────────────────────────────────────────────────────────────────▶│
 │                           │                   │                   │                   │                   │                   │──备份剪贴板    │
 │                           │                   │                   │                   │                   │                   │──写入文本      │
 │                           │                   │                   │                   │                   │                   │──模拟 Ctrl+V   │
 │                           │                   │                   │                   │                   │                   │──恢复剪贴板    │
 │                           │                   │◀──────────────────────────────────────────────────────────────────────────────────────────│
 │                           │                   │──────────────────────────────────────────────────────────────────────────────────────────────▶│
 │                           │                   │                   │                   │                   │                   │           显示 Success
 │                           │                   │                   │                   │                   │                   │           1.5s 后隐藏
 │                           │                   │                   │                   │                   │                   │               │
```

## 补充说明

- 当前版本强调“按住说话、松开即出字”的实时体验
- 如果 ASR 已经能满足要求，主链路默认不建议强依赖 LLM 纠错
- Right Shift 在不同键盘上的兼容性可能存在差异，后续可能继续优化或开放热键配置
