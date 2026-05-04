# 语音对话功能改造方案

## 目标

当前项目从“语音输入法”扩展为“双模式桌面语音助手”：

1. 输入模式
按住热键说话，识别后直接上屏到当前输入框。

2. 对话模式
按住热键说话，识别后发送给大模型，大模型返回文本或音频，程序自动播放语音回复，并可在控制台查看对话记录。

3. 模式切换
用户可在托盘菜单 / 控制台窗口中切换：
- 输入模式
- 对话模式
- 可选：混合模式（上屏 + 对话）

4. 打断与连续交互
用户在播放回复时再次按热键，应能立即打断播放并开始新一轮录音。

## 最终功能范围

### 1. 输入模式保留并继续可用

- 保留现有语音输入法能力
- 保留热键触发
- 保留 HUD 状态提示
- 保留 ASR 参数、热词表、上下文
- 保留文本注入链路

### 2. 新增语音对话模式

- 用户按住热键说话
- 程序进行 ASR
- 将识别文本发送给豆包/目标大模型
- 支持多轮上下文
- 模型返回文本或音频
- 程序自动播放语音回复
- 控制台中可查看用户说了什么、模型回了什么

### 3. 音频回复能力

- 支持模型直接返回音频
- 如果模型只返回文本，则调用 TTS 合成后播放
- 支持播放中断
- 支持播放状态显示（播放中 / 已完成 / 已中断 / 错误）

### 4. 对话会话管理

- 支持单轮 / 多轮上下文
- 支持清空当前会话
- 支持设置最大上下文轮数
- 支持模式切换时决定是否保留上下文

## 核心架构改造

### 1. 从“单一输入法编排器”升级为“多模式语音编排器”

当前 [VoiceInputOrchestrator.cs](E:\输入法\VoiceInputApp\Services\VoiceInputOrchestrator.cs) 主要围绕“识别后注入”设计。

改造目标：

- 输入模式：ASR -> 注入
- 对话模式：ASR -> LLM -> TTS/音频播放
- 混合模式：ASR -> 注入 + LLM/TTS

建议新增枚举：

```csharp
enum InteractionMode
{
    Input,
    Conversation,
    Hybrid
}
```

建议扩展状态机：

```csharp
enum VoiceInteractionState
{
    Idle,
    Connecting,
    Recording,
    Stopping,
    Recognizing,
    Injecting,
    Thinking,
    Synthesizing,
    Playing,
    Error
}
```

### 2. 新增对话服务层

建议新增接口：

```csharp
interface IConversationService
{
    Task<ConversationResponse> SendAsync(ConversationRequest request, CancellationToken cancellationToken);
}
```

职责：

- 向大模型发送文本
- 附带上下文
- 返回文本、音频地址或音频字节
- 处理模型接口错误

建议新增模型：

```csharp
class ConversationRequest
{
    public string UserText { get; set; }
    public IReadOnlyList<ConversationMessage> History { get; set; }
    public Language Language { get; set; }
}
```

```csharp
class ConversationResponse
{
    public string? Text { get; set; }
    public byte[]? AudioBytes { get; set; }
    public string? AudioMimeType { get; set; }
    public bool IsError { get; set; }
    public string? ErrorMessage { get; set; }
}
```

### 3. 新增音频播放服务

建议新增接口：

```csharp
interface IAudioPlaybackService
{
    Task PlayAsync(byte[] audioData, string mimeType, CancellationToken cancellationToken);
    void Stop();
    bool IsPlaying { get; }
}
```

职责：

- 播放模型返回音频
- 支持停止 / 打断
- 播放完成通知
- 统一处理播放错误

实现建议：

- 如果返回 mp3/wav，直接播放
- 如果返回流式音频，后续再扩展为边下边播

### 4. 新增 TTS 服务

如果模型接口不直接返回音频，需要引入：

```csharp
interface ITtsService
{
    Task<TtsResult> SynthesizeAsync(string text, Language language, CancellationToken cancellationToken);
}
```

职责：

- 文本转音频
- 返回音频字节
- 支持失败回退

## 现有模块需要修改的地方

### 1. VoiceInputOrchestrator

重点修改文件：
[VoiceInputOrchestrator.cs](E:\输入法\VoiceInputApp\Services\VoiceInputOrchestrator.cs)

需要改的内容：

- 增加模式判断
- 将“识别完成后的处理”抽成分支
- 把“文本注入”从默认唯一出口，改为一种输出策略
- 增加对话状态：
  - Thinking
  - Synthesizing
  - Playing
- 增加播放打断逻辑
- 录音开始前，如果当前正在播放，先停止播放
- 识别完成后：
  - 输入模式 -> 注入
  - 对话模式 -> 调模型 -> 播放音频
  - 混合模式 -> 注入 + 调模型

建议拆分方法：

```csharp
Task HandleInputModeAsync(...)
Task HandleConversationModeAsync(...)
Task HandleHybridModeAsync(...)
Task StopPlaybackIfNeededAsync()
```

### 2. AppSettings

重点修改文件：
[AppSettings.cs](E:\输入法\VoiceInputApp\Models\AppSettings.cs)

建议新增配置项：

```csharp
public InteractionMode Mode { get; set; }
public int MaxConversationTurns { get; set; }
public bool InterruptPlaybackOnHotkey { get; set; }
public bool AutoPlayResponseAudio { get; set; }
public bool ShowConversationInControlCenter { get; set; }
public string ConversationModelName { get; set; }
public string TtsVoice { get; set; }
public bool UseModelNativeAudio { get; set; }
```

### 3. 控制台窗口

重点修改文件：
[ControlCenterWindow.xaml](E:\输入法\VoiceInputApp\Windows\ControlCenterWindow.xaml)
[ControlCenterWindow.xaml.cs](E:\输入法\VoiceInputApp\Windows\ControlCenterWindow.xaml.cs)

控制台需要扩展成“状态中心 + 对话面板”。

新增内容：

- 当前模式切换
- 当前会话状态
- 最近一轮识别文本
- 最近一轮模型回复文本
- 清空会话按钮
- 是否自动播放音频开关
- TTS / 模型语音配置入口
- 当前播放状态

建议增加一个简单聊天区：

- 用户消息
- 助手消息
- 时间
- 错误提示

### 4. 托盘菜单

重点修改文件：
[TrayIconService.cs](E:\输入法\VoiceInputApp\Services\Tray\TrayIconService.cs)

托盘菜单新增：

- 模式切换
  - 输入模式
  - 对话模式
  - 混合模式
- 清空对话上下文
- 停止当前播放
- 打开对话控制台

### 5. HUD

重点修改文件：
[HudWindow.xaml](E:\输入法\VoiceInputApp\Windows\HudWindow.xaml)
[HudWindow.xaml.cs](E:\输入法\VoiceInputApp\Windows\HudWindow.xaml.cs)

HUD 需要覆盖新状态：

- 正在聆听
- 正在识别
- 正在思考
- 正在生成语音
- 正在播放回复
- 错误

建议 HUD 文案：

- 正在聆听...
- 识别中...
- 思考中...
- 生成语音...
- 正在播放回复...

增加一个“打断播放”反馈逻辑：

- 再次按热键时，HUD 短暂显示“已打断，重新聆听”

## 会话与上下文设计

### 1. 新增会话历史模型

```csharp
class ConversationMessage
{
    public string Role { get; set; } // user / assistant / system
    public string Content { get; set; }
    public DateTime Timestamp { get; set; }
}
```

建议新增会话存储组件：

```csharp
interface IConversationSessionStore
{
    IReadOnlyList<ConversationMessage> GetMessages();
    void AddUserMessage(string text);
    void AddAssistantMessage(string text);
    void Clear();
}
```

职责：

- 保留最近 N 轮上下文
- 提供给模型调用
- 提供给控制台显示

### 2. 上下文策略

建议：

- 默认保留最近 6 到 10 轮
- 超过上限自动裁剪
- 支持手动清空
- 输入模式与对话模式上下文分离
  - ASR 上下文
  - 对话上下文
- 不要把所有注入文本都直接喂给对话模型

## 接口与服务集成任务

### 1. 模型对话接口接入

任务项：

- 确认豆包语音对话接口能力
- 确认是否支持原生返回音频
- 确认请求格式
- 确认多轮上下文字段
- 确认错误码与限流策略
- 封装对话请求服务

### 2. TTS 接入

任务项：

- 确认使用豆包 TTS 还是第三方 TTS
- 封装文本转音频接口
- 支持音色配置
- 支持失败回退到纯文本回复

### 3. 音频播放

任务项：

- 选择播放组件
- 支持 mp3/wav 播放
- 支持播放中断
- 支持快速开始下一轮录音时中止当前音频
- 播放完成后更新 HUD 状态

## 交互策略任务

### 1. 热键交互

成熟方案要求：

- 输入模式和对话模式共用同一热键
- 录音中再次按键不应重入
- 播放中按键应：
  - 先停止播放
  - 再开始新一轮录音
- 连按防抖和释放丢失兜底仍然保留

### 2. 中断策略

需要统一这些规则：

- 正在 Thinking 时再次按热键怎么办
- 正在 Synthesizing 时再次按热键怎么办
- 正在 Playing 时再次按热键怎么办

建议统一：

- 再次按热键 = 中断当前输出，优先进入新录音

### 3. 输出策略

对话模式下支持两种展示：

- 只播语音，不上屏
- 播语音，同时把模型回复显示在控制台窗口

混合模式：

- 用户语音先上屏
- 模型再回复并播报

## 错误处理与恢复

### 1. 识别错误

- 保留当前已有 ASR 友好提示
- HUD 显示“识别失败”
- 播放链路不得被错误状态卡死

### 2. 模型调用错误

- 模型请求超时
- 网络失败
- 鉴权失败
- 返回内容为空

处理要求：

- HUD 显示“对话失败”
- 控制台保留本轮用户文本
- 可以不播放音频
- 状态回到 Idle

### 3. TTS/播放错误

- TTS 失败时：
  - 至少显示文本回复
- 播放失败时：
  - 记录日志
  - HUD 提示
  - 不影响下一轮录音

## 日志与调试

建议新增日志维度：

- 本轮模式
- 识别文本
- 对话请求耗时
- TTS 耗时
- 播放开始/结束/中断
- 会话历史长度
- 中断原因

建议新增性能日志：

- ASR ready in xxx ms
- Conversation replied in xxx ms
- TTS synthesized in xxx ms
- Playback started in xxx ms

## 测试任务

### 1. 功能测试

- 输入模式仍可正常上屏
- 对话模式可正常识别并返回音频
- 混合模式行为正确
- 模式切换后配置生效
- 控制台会话记录显示正确

### 2. 中断测试

- 播放中再次按热键能否立即打断
- Thinking 阶段再次按热键是否能正确取消
- 连续多轮语音对话是否会卡状态
- 快速按压热键是否会导致状态机混乱

### 3. 兼容性测试

- 普通输入框
- 浏览器文本框
- 聊天软件
- 不同麦克风设备
- 插拔设备后的恢复
- 长时间常驻后状态是否稳定

### 4. 性能测试

- 首轮对话延迟
- 连续多轮对话内存占用
- 播放中断响应时间
- HUD 动画是否卡顿
- 日志增长是否可控

## 建议新增的主要文件

建议新增这些服务/模型：

- `Services/Conversation/IConversationService.cs`
- `Services/Conversation/DoubaoConversationService.cs`
- `Services/Conversation/ConversationSessionStore.cs`
- `Services/Tts/ITtsService.cs`
- `Services/Tts/DoubaoTtsService.cs`
- `Services/Audio/IAudioPlaybackService.cs`
- `Services/Audio/AudioPlaybackService.cs`
- `Models/ConversationMessage.cs`
- `Models/ConversationRequest.cs`
- `Models/ConversationResponse.cs`

## 建议修改的主要文件

重点要改：

- [VoiceInputOrchestrator.cs](E:\输入法\VoiceInputApp\Services\VoiceInputOrchestrator.cs)
- [AppSettings.cs](E:\输入法\VoiceInputApp\Models\AppSettings.cs)
- [App.xaml.cs](E:\输入法\VoiceInputApp\App.xaml.cs)
- [ControlCenterWindow.xaml](E:\输入法\VoiceInputApp\Windows\ControlCenterWindow.xaml)
- [ControlCenterWindow.xaml.cs](E:\输入法\VoiceInputApp\Windows\ControlCenterWindow.xaml.cs)
- [TrayIconService.cs](E:\输入法\VoiceInputApp\Services\Tray\TrayIconService.cs)
- [HudWindow.xaml](E:\输入法\VoiceInputApp\Windows\HudWindow.xaml)
- [HudWindow.xaml.cs](E:\输入法\VoiceInputApp\Windows\HudWindow.xaml.cs)

## 最终完成标准

满足以下条件才算完整落地：

- 输入模式不退化
- 对话模式稳定可用
- 模型回复可自动播报
- 播放中可被热键打断
- 多轮上下文可控
- 控制台可查看对话
- 模式切换清晰
- 错误恢复完整
- 日志和状态可排查
- 长时间使用不容易卡死
