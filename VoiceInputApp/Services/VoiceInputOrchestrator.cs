using VoiceInputApp.Models;
using VoiceInputApp.Services.Audio;
using VoiceInputApp.Services.Hotkey;
using VoiceInputApp.Services.Injection;
using VoiceInputApp.Services.LLM;
using VoiceInputApp.Services.Logging;
using VoiceInputApp.Services.Notification;
using VoiceInputApp.Services.Settings;
using VoiceInputApp.Services.Transcription;
using System.Diagnostics;

namespace VoiceInputApp.Services;

public class VoiceInputOrchestrator : IDisposable
{
    private enum VoiceInputState
    {
        Idle,
        Connecting,
        Recording,
        Stopping,
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
    private readonly object _stateLock = new();

    private CancellationTokenSource? _recognitionCts;
    private Stopwatch? _sessionStopwatch;
    private string _partialText = string.Empty;
    private string _finalText = string.Empty;
    private string? _sessionId;
    private VoiceInputState _state = VoiceInputState.Idle;
    private HudInstance? _currentHud;

    public VoiceInputOrchestrator(
        IHotkeyMonitor hotkeyMonitor,
        AudioCaptureService audioCaptureService,
        ITranscriptionService transcriptionService,
        ITextInjectionService textInjectionService,
        ILlmRefinementService llmRefinementService,
        ISettingsService settingsService,
        INotificationService notificationService,
        ILogger logger,
        HudManager hudManager)
    {
        _hotkeyMonitor = hotkeyMonitor;
        _audioCaptureService = audioCaptureService;
        _transcriptionService = transcriptionService;
        _textInjectionService = textInjectionService;
        _llmRefinementService = llmRefinementService;
        _settingsService = settingsService;
        _notificationService = notificationService;
        _hudManager = hudManager;

        _hotkeyMonitor.KeyPressed += OnKeyPressed;
        _hotkeyMonitor.KeyReleased += OnKeyReleased;
        _audioCaptureService.AudioLevelUpdated += OnAudioLevelUpdated;
        _audioCaptureService.AudioDataAvailable += OnAudioDataAvailable;
    }

    public void Start()
    {
        _hotkeyMonitor.Start();
        _logger.Info("Voice Input Orchestrator started");
    }

    public void Stop()
    {
        _hotkeyMonitor.Stop();
        _logger.Info("Voice Input Orchestrator stopped");
    }

    private void OnKeyPressed(object? sender, HotkeyEventArgs e)
    {
        _ = StartRecordingAsync();
    }

    private void OnKeyReleased(object? sender, HotkeyEventArgs e)
    {
        _ = StopRecordingAsync();
    }

    private void OnAudioLevelUpdated(object? sender, AudioLevelEventArgs e)
    {
        _currentHud?.UpdateAudioLevel(e.Level);
    }

    private void OnAudioDataAvailable(object? sender, byte[] data)
    {
        string? sessionId;
        lock (_stateLock)
        {
            if (_state != VoiceInputState.Recording || string.IsNullOrWhiteSpace(_sessionId))
            {
                return;
            }

            sessionId = _sessionId;
        }

        _ = SendAudioAsync(data, sessionId);
    }

    private async Task StartRecordingAsync()
    {
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
            sessionId = _sessionId;
        }

        try
        {
            _recognitionCts = new CancellationTokenSource();

            var deviceIndex = _settingsService.Current.MicrophoneDeviceIndex;
            if (deviceIndex >= 0)
            {
                _audioCaptureService.SetDevice(deviceIndex);
                Log(sessionId, $"Using microphone device: {deviceIndex}");
            }

            _currentHud = _hudManager.CreateHud();
            _currentHud.UpdateState(HudState.Transcribing, "连接识别服务...");
            _currentHud.Show();

            Log(sessionId, "Connecting ASR session");
            await _transcriptionService.StartStreamingRecognitionAsync(
                _settingsService.Current.Language,
                sessionId,
                OnTranscriptionResult,
                _recognitionCts.Token);

            _audioCaptureService.StartCapture();
            _hotkeyMonitor.SetRecordingStarted(true);

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

        _hotkeyMonitor.SetRecordingStarted(false);

        try
        {
            Log(sessionId!, "Stopping recording");
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
                _finalText = result.Text;
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
            return;
        }

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
            hud?.UpdateState(HudState.Success, finalText);
            HideHudAfterDelay(hud, 1000);
        }
        else
        {
            _notificationService.Show("语音输入", "文本注入失败，请手动粘贴", NotificationType.Warning);
            hud?.UpdateState(HudState.Error, "注入失败");
            HideHudAfterDelay(hud, 800);
        }
    }

    private void HideHudAfterDelay(HudInstance? hud, int delayMs)
    {
        if (hud == null)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            await Task.Delay(delayMs);
            hud.HideWithAnimation();
        });
    }

    private async Task SendAudioAsync(byte[] data, string sessionId)
    {
        try
        {
            await _transcriptionService.SendAudioDataAsync(data, _recognitionCts?.Token ?? CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            _logger.Debug($"[{sessionId}] Audio send cancelled");
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
        lock (_stateLock)
        {
            _state = VoiceInputState.Idle;
            _partialText = string.Empty;
            _finalText = string.Empty;
            _sessionId = null;
        }

        _recognitionCts?.Dispose();
        _recognitionCts = null;
        _currentHud = null;
        _sessionStopwatch = null;
    }

    private void ReleaseSessionForNextRecording()
    {
        lock (_stateLock)
        {
            _state = VoiceInputState.Idle;
            _partialText = string.Empty;
            _finalText = string.Empty;
            _sessionId = null;
        }

        _recognitionCts?.Dispose();
        _recognitionCts = null;
    }

    private async Task FailSessionAsync(string message)
    {
        try
        {
            _hotkeyMonitor.SetRecordingStarted(false);
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

    public void Dispose()
    {
        _recognitionCts?.Cancel();
        _recognitionCts?.Dispose();
        _hotkeyMonitor.Stop();
    }
}
