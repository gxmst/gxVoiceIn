using VoiceInputApp.Models;
using VoiceInputApp.Services.Audio;
using VoiceInputApp.Services.Conversation;
using VoiceInputApp.Services.Hotkey;
using VoiceInputApp.Services.Injection;
using VoiceInputApp.Services.LLM;
using VoiceInputApp.Services.Logging;
using VoiceInputApp.Services.Notification;
using VoiceInputApp.Services.Settings;
using VoiceInputApp.Services.Transcription;
using VoiceInputApp.Services.Tts;
using System.Diagnostics;
using System.Threading.Channels;

namespace VoiceInputApp.Services;

public class VoiceInputOrchestrator : IDisposable
{
    private const int PostReleaseAudioTailMs = 200;
    private const float SilenceAutoStopThreshold = 0.035f;
    private static readonly TimeSpan AutoStopSilenceWindow = TimeSpan.FromMilliseconds(800);
    private static readonly TimeSpan AutoStopStableResultWindow = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan AutoStopMinimumSessionAge = TimeSpan.FromMilliseconds(800);
    private static readonly TimeSpan AutoStopPollInterval = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan AutoStopNoTextGrowthWindow = TimeSpan.FromMilliseconds(1200);
    private static readonly TimeSpan AutoStopMaxSessionAge = TimeSpan.FromMilliseconds(15000);

    private enum VoiceInputState
    {
        Idle,
        Connecting,
        Recording,
        Stopping,
        Thinking,
        Synthesizing,
        Playing,
        Error
    }

    private readonly IHotkeyMonitor _hotkeyMonitor;
    private readonly AudioCaptureService _audioCaptureService;
    private readonly ITranscriptionService _transcriptionService;
    private readonly ITextInjectionService _textInjectionService;
    private readonly ILlmRefinementService _llmRefinementService;
    private readonly ISettingsService _settingsService;
    private readonly INotificationService _notificationService;
    private readonly ILoggingService _logger = LoggingService.Instance;
    private readonly HudManager _hudManager;
    private readonly IConversationService _conversationService;
    private readonly IConversationSessionStore _conversationSessionStore;
    private readonly ITtsService _ttsService;
    private readonly IAudioPlaybackService _audioPlaybackService;
    private readonly object _stateLock = new();
    private readonly SemaphoreSlim _startStopSemaphore = new(1, 1);
    private readonly Channel<bool> _keyEventChannel = Channel.CreateUnbounded<bool>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Queue<string> _recentCommittedTexts = new();

    private CancellationTokenSource? _recognitionCts;
    private CancellationTokenSource? _responseCts;
    private Stopwatch? _sessionStopwatch;
    private string _partialText = string.Empty;
    private string _finalText = string.Empty;
    private string? _sessionId;
    private VoiceInputState _state = VoiceInputState.Idle;
    private HudInstance? _currentHud;
    private DateTimeOffset _lastAudioActivityAt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastFinalResultAt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastFinalTextLengthChangeAt = DateTimeOffset.MinValue;
    private string _latestFinalSnapshot = string.Empty;
    private CancellationTokenSource? _autoStopMonitorCts;
    private CancellationTokenSource? _keyEventLoopCts;

    private string _lastRecognizedText = string.Empty;
    private string _lastAssistantReply = string.Empty;

    public event EventHandler<VoiceInteractionSnapshot>? SnapshotChanged;

    public VoiceInputOrchestrator(
        IHotkeyMonitor hotkeyMonitor,
        AudioCaptureService audioCaptureService,
        ITranscriptionService transcriptionService,
        ITextInjectionService textInjectionService,
        ILlmRefinementService llmRefinementService,
        ISettingsService settingsService,
        INotificationService notificationService,
        ILogger logger,
        HudManager hudManager,
        IConversationService conversationService,
        IConversationSessionStore conversationSessionStore,
        ITtsService ttsService,
        IAudioPlaybackService audioPlaybackService)
    {
        _hotkeyMonitor = hotkeyMonitor;
        _audioCaptureService = audioCaptureService;
        _transcriptionService = transcriptionService;
        _textInjectionService = textInjectionService;
        _llmRefinementService = llmRefinementService;
        _settingsService = settingsService;
        _notificationService = notificationService;
        _hudManager = hudManager;
        _conversationService = conversationService;
        _conversationSessionStore = conversationSessionStore;
        _ttsService = ttsService;
        _audioPlaybackService = audioPlaybackService;

        _hotkeyMonitor.KeyPressed += OnKeyPressed;
        _hotkeyMonitor.KeyReleased += OnKeyReleased;
        _audioCaptureService.AudioLevelUpdated += OnAudioLevelUpdated;
        _audioCaptureService.AudioDataAvailable += OnAudioDataAvailable;
    }

    public void Start()
    {
        _hotkeyMonitor.Start();
        _keyEventLoopCts = new CancellationTokenSource();
        _ = ProcessKeyEventsAsync(_keyEventLoopCts.Token);
        _logger.Info("Voice Input Orchestrator started");
    }

    public void Stop()
    {
        _keyEventLoopCts?.Cancel();
        _hotkeyMonitor.Stop();
        _logger.Info("Voice Input Orchestrator stopped");
    }

    public void SetMode(InteractionMode mode)
    {
        if (_audioPlaybackService.IsPlaying)
        {
            _audioPlaybackService.Stop();
            _logger.Info("Playback stopped due to mode change");
        }

        CancelResponseIfNeeded();

        lock (_stateLock)
        {
            if (_state is VoiceInputState.Thinking or VoiceInputState.Synthesizing or VoiceInputState.Playing)
            {
                _logger.Info($"Force-reset state from {_state} to Idle during mode change");
                _state = VoiceInputState.Idle;
                _responseCts?.Dispose();
                _responseCts = null;
            }
        }

        _currentHud?.HideWithAnimation();
        _currentHud = null;

        _settingsService.Current.Mode = mode;
        _settingsService.Save(_settingsService.Current);
        _logger.Info($"Mode changed to {mode}");
        NotifySnapshotChanged();
    }

    public void StopPlayback()
    {
        _audioPlaybackService.Stop();
        NotifySnapshotChanged();
    }

    public void ClearConversationHistory()
    {
        _conversationSessionStore.Clear();
        _logger.Info("Conversation history cleared");
        NotifySnapshotChanged();
    }

    public VoiceInteractionSnapshot GetSnapshot()
    {
        var settings = _settingsService.Current;
        var messages = _conversationSessionStore.GetMessages();
        var conversationModel = string.IsNullOrWhiteSpace(settings.Llm.ConversationModel)
            ? settings.Llm.Model
            : settings.Llm.ConversationModel;

        lock (_stateLock)
        {
            return new VoiceInteractionSnapshot
            {
                Mode = settings.Mode,
                StateText = StateToChineseText(_state),
                IsPlaying = _state == VoiceInputState.Playing || _audioPlaybackService.IsPlaying,
                LastRecognizedText = _lastRecognizedText,
                LastAssistantReply = _lastAssistantReply,
                UpdatedAt = DateTime.Now,
                TriggerKeyDisplay = KeyCodeToDisplayName(settings.TriggerKey),
                LanguageDisplay = settings.Language.ToDisplayName(),
                TtsVoiceDisplay = settings.Llm.TtsVoice,
                ConversationModelDisplay = conversationModel,
                HasConversationHistory = messages.Count > 0,
                ConversationTurnCount = messages.Count / 2,
                CurrentSessionId = _sessionId,
                CurrentPlaybackState = _state switch
                {
                    VoiceInputState.Playing => "播放中",
                    VoiceInputState.Synthesizing => "合成中",
                    _ => "无"
                },
                IsRecording = _state == VoiceInputState.Recording || _state == VoiceInputState.Connecting,
                IsThinking = _state == VoiceInputState.Thinking,
                IsSynthesizing = _state == VoiceInputState.Synthesizing,
                UseNativeAudio = settings.Llm.UseModelNativeAudio
            };
        }
    }

    private static string KeyCodeToDisplayName(int keyCode)
    {
        return keyCode switch
        {
            0xA0 => "左Shift",
            0xA1 => "右Shift",
            0xA2 => "左Ctrl",
            0xA3 => "右Ctrl",
            0xA4 => "左Alt",
            0xA5 => "右Alt",
            0x12 => "Alt",
            0x11 => "Ctrl",
            0x10 => "Shift",
            0x14 => "CapsLock",
            0x20 => "Space",
            0x0D => "Enter",
            0x08 => "Backspace",
            0x1B => "Esc",
            0x70 => "F1",
            0x71 => "F2",
            0x72 => "F3",
            0x73 => "F4",
            0x74 => "F5",
            0x75 => "F6",
            0x76 => "F7",
            0x77 => "F8",
            0x78 => "F9",
            0x79 => "F10",
            0x7A => "F11",
            0x7B => "F12",
            _ => $"0x{keyCode:X2}"
        };
    }

    private static string StateToChineseText(VoiceInputState state)
    {
        return state switch
        {
            VoiceInputState.Idle => "空闲",
            VoiceInputState.Connecting => "连接中",
            VoiceInputState.Recording => "正在录音",
            VoiceInputState.Stopping => "识别中",
            VoiceInputState.Thinking => "思考中",
            VoiceInputState.Synthesizing => "生成语音中",
            VoiceInputState.Playing => "正在播放",
            VoiceInputState.Error => "错误",
            _ => state.ToString()
        };
    }

    private static string TruncateForHud(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }

    private void OnKeyPressed(object? sender, HotkeyEventArgs e)
    {
        _keyEventChannel.Writer.TryWrite(true);
    }

    private void OnKeyReleased(object? sender, HotkeyEventArgs e)
    {
        _keyEventChannel.Writer.TryWrite(false);
    }

    private async Task ProcessKeyEventsAsync(CancellationToken cancellationToken)
    {
        await foreach (var isKeyDown in _keyEventChannel.Reader.ReadAllAsync(cancellationToken))
        {
            try
            {
                await _startStopSemaphore.WaitAsync(cancellationToken);
                try
                {
                    if (isKeyDown)
                    {
                        await StartRecordingAsync();
                    }
                    else
                    {
                        await StopRecordingAsync();
                    }
                }
                finally
                {
                    _startStopSemaphore.Release();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Error($"Error processing key event (KeyDown={isKeyDown}): {ex.Message}", ex);
            }
        }
    }

    private void OnAudioLevelUpdated(object? sender, AudioLevelEventArgs e)
    {
        if (e.Level >= SilenceAutoStopThreshold)
        {
            lock (_stateLock)
            {
                _lastAudioActivityAt = DateTimeOffset.UtcNow;
            }
        }

        _currentHud?.UpdateAudioLevel(e.Level);
    }

    private void OnAudioDataAvailable(object? sender, byte[] data)
    {
        string? sessionId;
        lock (_stateLock)
        {
            if ((_state != VoiceInputState.Recording && _state != VoiceInputState.Stopping) || string.IsNullOrWhiteSpace(_sessionId))
            {
                return;
            }

            sessionId = _sessionId;
        }

        _ = SendAudioAsync(data, sessionId);
    }

    private async Task StartRecordingAsync()
    {
        if (_settingsService.Current.InterruptPlaybackOnHotkey)
        {
            bool wasPlaying = false;

            if (_audioPlaybackService.IsPlaying)
            {
                _audioPlaybackService.Stop();
                wasPlaying = true;
                _logger.Info("Playback interrupted by hotkey");
            }

            CancelResponseIfNeeded();

            bool wasInterrupted = false;
            for (int i = 0; i < 10; i++)
            {
                VoiceInputState currentState;
                lock (_stateLock)
                {
                    currentState = _state;
                }

                if (currentState == VoiceInputState.Idle)
                {
                    break;
                }

                if (currentState is VoiceInputState.Thinking or VoiceInputState.Synthesizing or VoiceInputState.Playing)
                {
                    lock (_stateLock)
                    {
                        _state = VoiceInputState.Idle;
                    }

                    _responseCts?.Dispose();
                    _responseCts = null;
                    wasInterrupted = true;
                    _logger.Info($"Force-reset state from {currentState} to Idle for hotkey interrupt");
                    break;
                }

                await Task.Delay(20);
            }

            if (wasPlaying || wasInterrupted)
            {
                if (_currentHud != null)
                {
                    _currentHud.UpdateState(HudState.Interrupted, "已打断，重新聆听...");
                    var interruptedHud = _currentHud;
                    _currentHud = null;
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(600);
                        interruptedHud.HideWithAnimation();
                    });
                }
            }
        }

        string sessionId;
        lock (_stateLock)
        {
            if (_state != VoiceInputState.Idle)
            {
                _logger.Debug($"Ignoring start request because state is {_state}");
                return;
            }

            _state = VoiceInputState.Connecting;
            _partialText = string.Empty;
            _finalText = string.Empty;
            _sessionId = Guid.NewGuid().ToString("N")[..8];
            _sessionStopwatch = Stopwatch.StartNew();
            _lastAudioActivityAt = DateTimeOffset.UtcNow;
            _lastFinalResultAt = DateTimeOffset.MinValue;
            _lastFinalTextLengthChangeAt = DateTimeOffset.UtcNow;
            _latestFinalSnapshot = string.Empty;
            sessionId = _sessionId;
        }

        try
        {
            _recognitionCts = new CancellationTokenSource();

            var deviceName = _settingsService.Current.MicrophoneDeviceName;
            var deviceIndex = _settingsService.Current.MicrophoneDeviceIndex;

            if (!string.IsNullOrWhiteSpace(deviceName))
            {
                _audioCaptureService.SetDeviceByName(deviceName);
                Log(sessionId, $"Using microphone device by name: '{deviceName}'");
            }
            else if (deviceIndex >= 0)
            {
                _audioCaptureService.SetDevice(deviceIndex);
                Log(sessionId, $"Using microphone device by index: {deviceIndex}");
            }

            _currentHud = _hudManager.CreateHud();
            _currentHud.UpdateState(HudState.Transcribing, "连接识别服务...");
            _currentHud.Show();

            Log(sessionId, "Connecting ASR session");
            await _transcriptionService.StartStreamingRecognitionAsync(
                _settingsService.Current.Language,
                sessionId,
                GetContextTexts(),
                OnTranscriptionResult,
                _recognitionCts.Token);

            _audioCaptureService.StartCapture();

            lock (_stateLock)
            {
                if (_sessionId != sessionId)
                {
                    return;
                }

                _state = VoiceInputState.Recording;
            }

            Log(sessionId, "Recording started");
            Log(sessionId, $"Ready in {_sessionStopwatch?.ElapsedMilliseconds ?? 0}ms");
            _currentHud.UpdateState(HudState.Listening, "正在聆听...");
            StartAutoStopMonitor(sessionId);
            NotifySnapshotChanged();
        }
        catch (Exception ex)
        {
            _logger.Error($"[{sessionId}] Failed to start recording", ex);
            _notificationService.Show("语音输入", $"启动录音失败: {ex.Message}", NotificationType.Error);
            _currentHud?.HideWithAnimation();
            ResetSession();
        }
    }

    private async Task StopRecordingAsync()
    {
        string? sessionId;
        lock (_stateLock)
        {
            if (_state != VoiceInputState.Recording)
            {
                _logger.Debug($"Ignoring stop request because state is {_state}");
                return;
            }

            _state = VoiceInputState.Stopping;
            sessionId = _sessionId;
        }

        try
        {
            Log(sessionId!, "Stopping recording");
            if (PostReleaseAudioTailMs > 0)
            {
                Log(sessionId!, $"Keeping capture alive for tail audio: {PostReleaseAudioTailMs}ms");
                await Task.Delay(PostReleaseAudioTailMs);
            }
            _audioCaptureService.StopCapture();

            var stopResult = await _transcriptionService.StopRecognitionAsync(_recognitionCts?.Token ?? CancellationToken.None);
            if (stopResult is { IsError: true })
            {
                throw new InvalidOperationException(stopResult.ErrorMessage);
            }

            HudInstance? hud;
            lock (_stateLock)
            {
                if (!string.IsNullOrWhiteSpace(stopResult?.Text))
                {
                    _finalText = stopResult.Text;
                }

                hud = _currentHud;
                _currentHud = null;
            }

            var textToProcess = GetBestTranscript();
            ReleaseSessionForNextRecording();
            Log(sessionId!, $"Processing text: '{textToProcess}'");
            Log(sessionId!, $"ASR completed in {GetElapsedMilliseconds()}ms");
            hud?.UpdateState(HudState.Transcribing, string.IsNullOrEmpty(textToProcess) ? "识别中..." : textToProcess);

            _ = ProcessResultAsync(textToProcess, sessionId!, hud);
        }
        catch (Exception ex)
        {
            _logger.Error($"[{sessionId}] Failed to stop recording", ex);
            _notificationService.Show("语音输入", $"识别失败: {ex.Message}", NotificationType.Error);
            _currentHud?.HideWithAnimation();
            ResetSession();
        }
    }

    private void OnTranscriptionResult(TranscriptionResult result)
    {
        string? sessionId;
        VoiceInputState stateSnapshot;
        lock (_stateLock)
        {
            sessionId = _sessionId;
            stateSnapshot = _state;
        }

        _logger.Debug($"[{sessionId}] OnTranscriptionResult called. IsError={result.IsError}, Text='{result.Text}', IsFinal={result.IsFinal}, State={stateSnapshot}");

        if (result.IsError)
        {
            _logger.Error($"[{sessionId}] Transcription error: {result.ErrorMessage}");
            _notificationService.Show("语音输入", result.ErrorMessage, NotificationType.Error);
            lock (_stateLock)
            {
                _state = VoiceInputState.Error;
            }

            _ = FailSessionAsync(result.ErrorMessage);
            return;
        }

        if (string.IsNullOrWhiteSpace(result.Text))
        {
            _logger.Warning($"[{sessionId}] Empty transcription result received");
            return;
        }

        lock (_stateLock)
        {
            if (result.IsFinal)
            {
                var now = DateTimeOffset.UtcNow;
                var previousLength = _finalText.Length;
                _finalText = result.Text;
                _latestFinalSnapshot = result.Text;
                _lastFinalResultAt = now;
                if (result.Text.Length != previousLength)
                {
                    _lastFinalTextLengthChangeAt = now;
                }
            }
            else
            {
                _partialText = result.Text;
            }
        }

        if (stateSnapshot == VoiceInputState.Recording)
        {
            _currentHud?.UpdateState(HudState.Listening, result.Text);
        }
    }

    private async Task ProcessResultAsync(string text, string sessionId, HudInstance? hud)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            _logger.Warning($"[{sessionId}] No transcription result to process");
            hud?.UpdateState(HudState.Error, "未识别到语音");
            HideHudAfterDelay(hud, 800);
            NotifySnapshotChanged();
            return;
        }

        _lastRecognizedText = text;

        var mode = _settingsService.Current.Mode;
        switch (mode)
        {
            case InteractionMode.Input:
                await HandleInputModeAsync(text, sessionId, hud);
                break;
            case InteractionMode.Conversation:
                await HandleConversationModeAsync(text, sessionId, hud);
                break;
            case InteractionMode.Hybrid:
                await HandleHybridModeAsync(text, sessionId, hud);
                break;
        }
    }

    private async Task HandleInputModeAsync(string text, string sessionId, HudInstance? hud)
    {
        var finalText = text;
        if (_settingsService.Current.LlmEnabled && _llmRefinementService.IsConfigured)
        {
            hud?.UpdateState(HudState.Refining, "润色中...");

            try
            {
                finalText = await _llmRefinementService.RefineAsync(text, _settingsService.Current.Language);
                _logger.Info($"[{sessionId}] LLM refined: {finalText}");
                hud?.UpdateState(HudState.Refining, finalText);
            }
            catch (Exception ex)
            {
                _logger.Error($"[{sessionId}] LLM refinement failed", ex);
            }
        }

        _logger.Info($"[{sessionId}] Injecting text: '{finalText}'");
        var success = await _textInjectionService.InjectTextAsync(finalText);
        Log(sessionId, $"Injection finished in {GetElapsedMilliseconds()}ms");
        if (success)
        {
            RememberCommittedText(finalText);
            hud?.UpdateState(HudState.Success, finalText);
            HideHudAfterDelay(hud, 1000);
        }
        else
        {
            _notificationService.Show("语音输入", "文本注入失败，请手动粘贴", NotificationType.Warning);
            hud?.UpdateState(HudState.Error, "注入失败");
            HideHudAfterDelay(hud, 800);
        }

        NotifySnapshotChanged();
    }

    private async Task HandleConversationModeAsync(string text, string sessionId, HudInstance? hud)
    {
        _responseCts = new CancellationTokenSource();
        var ct = _responseCts.Token;

        try
        {
            lock (_stateLock) { _state = VoiceInputState.Thinking; }
            hud?.UpdateState(HudState.Thinking, $"思考中: {TruncateForHud(text, 20)}");
            NotifySnapshotChanged();

            var history = _conversationSessionStore.GetMessages();

            var request = new ConversationRequest
            {
                UserText = text,
                History = history,
                Language = _settingsService.Current.Language
            };

            _conversationSessionStore.AddUserMessage(text);

            var response = await _conversationService.SendAsync(request, ct);
            if (response.IsError)
            {
                hud?.UpdateState(HudState.Error, response.ErrorMessage ?? "对话失败");
                HideHudAfterDelay(hud, 1500);
                _logger.Warning($"[{sessionId}] Conversation error: {response.ErrorMessage}");
                NotifySnapshotChanged();
                return;
            }

            var replyText = response.Text ?? string.Empty;
            _lastAssistantReply = replyText;
            _conversationSessionStore.AddAssistantMessage(replyText);

            if (response.AudioBytes != null && response.AudioBytes.Length > 0)
            {
                await PlayAudioAsync(response.AudioBytes, response.AudioMimeType ?? "audio/mpeg", sessionId, hud, ct, replyText);
            }
            else if (!string.IsNullOrWhiteSpace(replyText))
            {
                await SynthesizeAndPlayAsync(replyText, sessionId, hud, ct);
            }

            hud?.UpdateState(HudState.Success, replyText);
            HideHudAfterDelay(hud, 1500);
        }
        catch (OperationCanceledException)
        {
            _logger.Info($"[{sessionId}] Conversation cancelled by user");
            try
            {
                hud?.UpdateState(HudState.Interrupted, "已打断");
                HideHudAfterDelay(hud, 800);
            }
            catch (Exception hudEx)
            {
                _logger.Debug($"[{sessionId}] HUD update after cancel failed: {hudEx.Message}");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"[{sessionId}] Conversation failed", ex);
            hud?.UpdateState(HudState.Error, "对话失败");
            HideHudAfterDelay(hud, 1500);
        }
        finally
        {
            lock (_stateLock) { _state = VoiceInputState.Idle; }
            _responseCts?.Dispose();
            _responseCts = null;
            NotifySnapshotChanged();
        }
    }

    private async Task HandleHybridModeAsync(string text, string sessionId, HudInstance? hud)
    {
        var injectText = text;
        if (_settingsService.Current.LlmEnabled && _llmRefinementService.IsConfigured)
        {
            try
            {
                injectText = await _llmRefinementService.RefineAsync(text, _settingsService.Current.Language);
            }
            catch (Exception ex)
            {
                _logger.Error($"[{sessionId}] LLM refinement failed in hybrid mode", ex);
            }
        }

        _logger.Info($"[{sessionId}] Injecting text (hybrid): '{injectText}'");
        var success = await _textInjectionService.InjectTextAsync(injectText);
        if (success)
        {
            RememberCommittedText(injectText);
        }

        await HandleConversationModeAsync(text, sessionId, hud);
    }

    private async Task SynthesizeAndPlayAsync(string text, string sessionId, HudInstance? hud, CancellationToken ct)
    {
        if (!_ttsService.IsConfigured)
        {
            _logger.Warning($"[{sessionId}] TTS not configured, skipping audio playback");
            return;
        }

        lock (_stateLock) { _state = VoiceInputState.Synthesizing; }
        hud?.UpdateState(HudState.Synthesizing, "生成语音中...");
        NotifySnapshotChanged();

        var ttsResult = await _ttsService.SynthesizeAsync(text, _settingsService.Current.Language, ct);
        await PlayAudioAsync(ttsResult.AudioBytes, ttsResult.MimeType, sessionId, hud, ct, text);
    }

    private async Task PlayAudioAsync(byte[] audioData, string mimeType, string sessionId, HudInstance? hud, CancellationToken ct, string? displayText = null)
    {
        lock (_stateLock) { _state = VoiceInputState.Playing; }
        var hudText = !string.IsNullOrWhiteSpace(displayText) ? $"回复: {TruncateForHud(displayText, 25)}" : "正在播放回复...";
        hud?.UpdateState(HudState.Speaking, hudText);
        NotifySnapshotChanged();

        try
        {
            await _audioPlaybackService.PlayAsync(audioData, mimeType, ct);
        }
        catch (OperationCanceledException)
        {
            _logger.Info($"[{sessionId}] Audio playback cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error($"[{sessionId}] Audio playback failed", ex);
        }
    }

    private void CancelResponseIfNeeded()
    {
        _responseCts?.Cancel();
    }

    private void HideHudAfterDelay(HudInstance? hud, int delayMs)
    {
        if (hud == null) return;

        _ = Task.Run(async () =>
        {
            await Task.Delay(delayMs);
            hud.HideWithAnimation();
        });
    }

    private async Task SendAudioAsync(byte[] data, string sessionId)
    {
        CancellationToken token;
        lock (_stateLock)
        {
            if (_state != VoiceInputState.Recording && _state != VoiceInputState.Stopping)
            {
                return;
            }
            token = _recognitionCts?.Token ?? CancellationToken.None;
        }

        try
        {
            await _transcriptionService.SendAudioDataAsync(data, token);
        }
        catch (OperationCanceledException)
        {
            _logger.Debug($"[{sessionId}] Audio send cancelled");
        }
        catch (ObjectDisposedException)
        {
            _logger.Debug($"[{sessionId}] Audio send skipped, session already disposed");
        }
        catch (Exception ex)
        {
            _logger.Error($"[{sessionId}] Failed to send audio data", ex);
        }
    }

    private string GetBestTranscript()
    {
        lock (_stateLock)
        {
            return !string.IsNullOrWhiteSpace(_finalText) ? _finalText : _partialText;
        }
    }

    private void ResetSession()
    {
        _autoStopMonitorCts?.Cancel();
        _autoStopMonitorCts?.Dispose();
        _keyEventLoopCts?.Cancel();
        _keyEventLoopCts?.Dispose();
        _autoStopMonitorCts = null;

        lock (_stateLock)
        {
            _state = VoiceInputState.Idle;
            _partialText = string.Empty;
            _finalText = string.Empty;
            _sessionId = null;
            _lastAudioActivityAt = DateTimeOffset.MinValue;
            _lastFinalResultAt = DateTimeOffset.MinValue;
            _lastFinalTextLengthChangeAt = DateTimeOffset.MinValue;
            _latestFinalSnapshot = string.Empty;
        }

        _recognitionCts?.Dispose();
        _recognitionCts = null;
        _currentHud = null;
        _sessionStopwatch = null;
        NotifySnapshotChanged();
    }

    private void ReleaseSessionForNextRecording()
    {
        _autoStopMonitorCts?.Cancel();
        _autoStopMonitorCts?.Dispose();
        _autoStopMonitorCts = null;

        lock (_stateLock)
        {
            _state = VoiceInputState.Idle;
            _partialText = string.Empty;
            _finalText = string.Empty;
            _sessionId = null;
            _lastAudioActivityAt = DateTimeOffset.MinValue;
            _lastFinalResultAt = DateTimeOffset.MinValue;
            _lastFinalTextLengthChangeAt = DateTimeOffset.MinValue;
            _latestFinalSnapshot = string.Empty;
        }

        _recognitionCts?.Dispose();
        _recognitionCts = null;
    }

    private async Task FailSessionAsync(string message)
    {
        try
        {
            if (_audioCaptureService.IsCapturing)
            {
                _audioCaptureService.StopCapture();
            }
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to stop capture during error cleanup: {ex.Message}");
        }

        _currentHud?.UpdateState(HudState.Error, message);
        await Task.Delay(800);
        _currentHud?.HideWithAnimation();
        ResetSession();
    }

    private void Log(string sessionId, string message)
    {
        _logger.Info($"[{sessionId}] {message}");
    }

    private long GetElapsedMilliseconds()
    {
        lock (_stateLock)
        {
            return _sessionStopwatch?.ElapsedMilliseconds ?? 0;
        }
    }

    private IReadOnlyList<string> GetContextTexts()
    {
        lock (_stateLock)
        {
            return _recentCommittedTexts.ToArray();
        }
    }

    private void RememberCommittedText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        lock (_stateLock)
        {
            _recentCommittedTexts.Enqueue(text);
            while (_recentCommittedTexts.Count > 3)
            {
                _recentCommittedTexts.Dequeue();
            }
        }
    }

    private void NotifySnapshotChanged()
    {
        try
        {
            SnapshotChanged?.Invoke(this, GetSnapshot());
        }
        catch
        {
        }
    }

    private void StartAutoStopMonitor(string sessionId)
    {
        _autoStopMonitorCts?.Cancel();
        _autoStopMonitorCts?.Dispose();
        _autoStopMonitorCts = new CancellationTokenSource();
        var cancellationToken = _autoStopMonitorCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(AutoStopPollInterval, cancellationToken);

                    bool shouldEvaluate;
                    string? stableText;
                    DateTimeOffset lastAudioActivityAt;
                    DateTimeOffset lastFinalResultAt;
                    DateTimeOffset lastFinalTextLengthChangeAt;
                    int currentFinalTextLength;
                    long sessionAgeMs;

                    lock (_stateLock)
                    {
                        sessionAgeMs = _sessionStopwatch?.ElapsedMilliseconds ?? 0;
                        shouldEvaluate =
                            _state == VoiceInputState.Recording &&
                            string.Equals(_sessionId, sessionId, StringComparison.Ordinal) &&
                            !string.IsNullOrWhiteSpace(_latestFinalSnapshot);
                        stableText = _latestFinalSnapshot;
                        currentFinalTextLength = _finalText.Length;
                        lastAudioActivityAt = _lastAudioActivityAt;
                        lastFinalResultAt = _lastFinalResultAt;
                        lastFinalTextLengthChangeAt = _lastFinalTextLengthChangeAt;
                    }

                    if (!shouldEvaluate)
                    {
                        continue;
                    }

                    var now = DateTimeOffset.UtcNow;
                    var sessionAge = TimeSpan.FromMilliseconds(sessionAgeMs);
                    var silenceDuration = now - lastAudioActivityAt;
                    var stableResultDuration = now - lastFinalResultAt;

                    if (sessionAge > AutoStopMaxSessionAge)
                    {
                        _logger.Info($"[{sessionId}] Auto-stop: max session age reached ({sessionAge.TotalMilliseconds:F0}ms)");
                        _keyEventChannel.Writer.TryWrite(false);
                        return;
                    }

                    if (silenceDuration > AutoStopSilenceWindow && stableResultDuration > AutoStopStableResultWindow && sessionAge > AutoStopMinimumSessionAge)
                    {
                        _logger.Info($"[{sessionId}] Auto-stop: silence + stable result (silence={silenceDuration.TotalMilliseconds:F0}ms, stable={stableResultDuration.TotalMilliseconds:F0}ms)");
                        _keyEventChannel.Writer.TryWrite(false);
                        return;
                    }

                    var textGrowthDuration = now - lastFinalTextLengthChangeAt;
                    if (textGrowthDuration > AutoStopNoTextGrowthWindow && stableResultDuration > AutoStopStableResultWindow && sessionAge > AutoStopMinimumSessionAge)
                    {
                        _logger.Info($"[{sessionId}] Auto-stop: no text growth for {textGrowthDuration.TotalMilliseconds:F0}ms");
                        _keyEventChannel.Writer.TryWrite(false);
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.Error($"[{sessionId}] Auto-stop monitor error", ex);
            }
        }, cancellationToken);
    }

    public void Dispose()
    {
        _hotkeyMonitor.KeyPressed -= OnKeyPressed;
        _hotkeyMonitor.KeyReleased -= OnKeyReleased;
        _audioCaptureService.AudioLevelUpdated -= OnAudioLevelUpdated;
        _audioCaptureService.AudioDataAvailable -= OnAudioDataAvailable;

        _responseCts?.Cancel();
        _responseCts?.Dispose();
        _recognitionCts?.Cancel();
        _recognitionCts?.Dispose();
        _autoStopMonitorCts?.Cancel();
        _autoStopMonitorCts?.Dispose();

        if (_audioPlaybackService is IDisposable disposablePlayback)
        {
            disposablePlayback.Dispose();
        }
    }
}
