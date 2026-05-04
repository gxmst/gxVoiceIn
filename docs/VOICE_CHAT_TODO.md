# 语音对话功能 TODO

## 当前已完成的最小版本

当前代码已经补上这些能力：

- 新增交互模式：
  - `Input`
  - `Conversation`
  - `Hybrid`
- 新增对话服务：
  - `OpenAiConversationService`
- 新增会话上下文存储：
  - `ConversationSessionStore`
- 新增 TTS 服务：
  - `OpenAiTtsService`
- 新增音频播放服务：
  - `AudioPlaybackService`
- `VoiceInputOrchestrator` 已支持：
  - 输入模式：识别后注入
  - 对话模式：识别 -> 对话 -> TTS/播放
  - 混合模式：识别 -> 注入 -> 对话 -> TTS/播放
- 控制台窗口已支持：
  - 模式切换
  - 查看最近识别文本
  - 查看最近助手回复
  - 查看会话历史
  - 停止播放
  - 清空会话
- 托盘菜单已支持：
  - 模式切换
  - 停止播放
  - 清空会话

## 需要注意的当前实现约束

- 当前“对话”和“TTS”都默认走 OpenAI 兼容接口：
  - `/chat/completions`
  - `/audio/speech`
- 当前复用了已有 LLM 配置：
  - `BaseUrl`
  - `ApiKey`
  - `Model`
- 如果没有额外配置 `ConversationModel`，则默认用 `Model`
- TTS 暂时默认：
  - `TtsModel = tts-1`
  - `TtsVoice = alloy`
- 当前版本已经通过代码层编译验证：
  - `dotnet msbuild VoiceInputApp\\VoiceInputApp.csproj /t:Compile /p:UseAppHost=false`

## 优先级 P0：必须继续收口的部分

### 1. 对话 / TTS 配置界面补全

当前 [LlmSettingsWindow.xaml](E:\输入法\VoiceInputApp\Windows\LlmSettingsWindow.xaml) 还只有：

- `BaseUrl`
- `ApiKey`
- `Model`

需要补充：

- `ConversationModel`
- `TtsModel`
- `TtsVoice`
- `UseModelNativeAudio`

对应文件：

- [LlmSettingsWindow.xaml](E:\输入法\VoiceInputApp\Windows\LlmSettingsWindow.xaml)
- [LlmSettingsWindow.xaml.cs](E:\输入法\VoiceInputApp\Windows\LlmSettingsWindow.xaml.cs)
- [LlmSettingsViewModel.cs](E:\输入法\VoiceInputApp\ViewModels\LlmSettingsViewModel.cs)

### 2. 控制台状态显示优化

当前控制台能看，但还比较朴素。

需要继续补：

- 当前状态中文映射，不要直接显示 enum 英文
- 更明确显示：
  - 正在录音
  - 正在识别
  - 正在思考
  - 正在生成语音
  - 正在播放
- 增加：
  - 当前热键
  - 当前语言
  - 当前 TTS 音色

对应文件：

- [ControlCenterWindow.xaml](E:\输入法\VoiceInputApp\Windows\ControlCenterWindow.xaml)
- [ControlCenterWindow.xaml.cs](E:\输入法\VoiceInputApp\Windows\ControlCenterWindow.xaml.cs)

### 3. 热键打断链路再补完整

当前已支持：

- 播放中按热键停止播放
- 思考中 / 合成中按热键取消响应链路

还需要继续验证和加固：

- `Thinking` 被取消后是否一定能立刻进入新录音
- `Synthesizing` 被取消后是否会残留状态
- 高频快速按热键时是否会与 `_startStopSemaphore` 或 `_responseCts` 打架

重点检查文件：

- [VoiceInputOrchestrator.cs](E:\输入法\VoiceInputApp\Services\VoiceInputOrchestrator.cs)

## 优先级 P1：功能完整性补足

### 4. 原生音频回复支持

当前实现是：

- 优先使用 `ConversationResponse.AudioBytes`
- 没有音频时再用 TTS

但现在 `OpenAiConversationService` 还只取文本，没有真正请求“原生音频输出”。

需要补：

- 如果目标模型支持 audio output
- 在请求中增加音频返回参数
- 正确解析音频内容 / 音频 URL / base64 音频

对应文件：

- [OpenAiConversationService.cs](E:\输入法\VoiceInputApp\Services\Conversation\OpenAiConversationService.cs)

### 5. 播放中断与异常处理

当前 `AudioPlaybackService` 已能播和停，但还不够产品化。

需要补：

- 播放异常更明确日志
- 播放结束事件更明确地反馈到 orchestrator
- 停止播放后避免多次重复清理
- 如果临时文件被占用，增加更稳的删除策略

对应文件：

- [AudioPlaybackService.cs](E:\输入法\VoiceInputApp\Services\Audio\AudioPlaybackService.cs)

### 6. HUD 新状态打磨

虽然 `HudState` 已经扩展了：

- `Thinking`
- `Speaking`

但还需要继续补：

- 为 `Thinking` / `Speaking` 定制更清楚的文案和视觉
- 合成语音阶段最好有独立文案，不要还复用 `Refining`
- 播放中再次按热键时，HUD 给出“已打断，重新聆听”的短反馈

对应文件：

- [HudState.cs](E:\输入法\VoiceInputApp\Models\HudState.cs)
- [Converters.cs](E:\输入法\VoiceInputApp\Converters.cs)
- [HudWindow.xaml](E:\输入法\VoiceInputApp\Windows\HudWindow.xaml)
- [HudWindow.xaml.cs](E:\输入法\VoiceInputApp\Windows\HudWindow.xaml.cs)

### 7. 控制台聊天区可读性优化

当前聊天历史只是纯文本堆叠。

建议继续补：

- 区分 user / assistant 的视觉样式
- 支持时间
- 支持当前会话长度显示
- 支持清空后立即刷新

对应文件：

- [ControlCenterWindow.xaml](E:\输入法\VoiceInputApp\Windows\ControlCenterWindow.xaml)
- [ControlCenterWindow.xaml.cs](E:\输入法\VoiceInputApp\Windows\ControlCenterWindow.xaml.cs)

## 优先级 P2：体验与架构优化

### 8. 会话历史持久化

当前 `ConversationSessionStore` 只在内存里保存。

可以考虑：

- 程序退出后不保留
- 或者增加“最近一轮会话落盘”
- 或者增加“启动时是否恢复最近会话”开关

对应文件：

- [ConversationSessionStore.cs](E:\输入法\VoiceInputApp\Services\Conversation\ConversationSessionStore.cs)

### 9. 更清晰的状态快照模型

当前 `VoiceInteractionSnapshot` 已经有：

- `Mode`
- `StateText`
- `IsPlaying`
- `LastRecognizedText`
- `LastAssistantReply`

可以继续扩展：

- `CurrentSessionId`
- `CurrentTriggerKey`
- `CurrentLanguage`
- `HasConversationHistory`
- `CurrentPlaybackState`

对应文件：

- [VoiceInteractionSnapshot.cs](E:\输入法\VoiceInputApp\Models\VoiceInteractionSnapshot.cs)
- [VoiceInputOrchestrator.cs](E:\输入法\VoiceInputApp\Services\VoiceInputOrchestrator.cs)

### 10. 模式和配置关系再梳理

当前模式切换已经可用，但后续要继续整理：

- 输入模式下是否保留会话历史
- 切到输入模式时是否自动停止播放
- 混合模式下注入与对话失败时怎么提示

对应文件：

- [VoiceInputOrchestrator.cs](E:\输入法\VoiceInputApp\Services\VoiceInputOrchestrator.cs)
- [TrayIconService.cs](E:\输入法\VoiceInputApp\Services\Tray\TrayIconService.cs)
- [ControlCenterWindow.xaml.cs](E:\输入法\VoiceInputApp\Windows\ControlCenterWindow.xaml.cs)

## 建议下一个模型优先做的顺序

建议按这个顺序继续：

1. 先补 LLM / TTS 设置界面
2. 再补 HUD 的新状态文案和视觉反馈
3. 然后做原生音频回复支持
4. 最后再做聊天区和会话持久化优化

## 交接说明

下一个模型开始前，优先阅读：

- [VOICE_CHAT_PLAN.md](E:\输入法\docs\VOICE_CHAT_PLAN.md)
- [VOICE_CHAT_TODO.md](E:\输入法\docs\VOICE_CHAT_TODO.md)
- [VoiceInputOrchestrator.cs](E:\输入法\VoiceInputApp\Services\VoiceInputOrchestrator.cs)
- [ControlCenterWindow.xaml](E:\输入法\VoiceInputApp\Windows\ControlCenterWindow.xaml)
- [ControlCenterWindow.xaml.cs](E:\输入法\VoiceInputApp\Windows\ControlCenterWindow.xaml.cs)

优先理解当前已经做好的最小版，不要重新推翻主链路。
