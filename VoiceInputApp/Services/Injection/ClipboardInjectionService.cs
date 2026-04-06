using System.Runtime.InteropServices;
using VoiceInputApp.Services.Logging;

namespace VoiceInputApp.Services.Injection;

public class ClipboardInjectionService : ITextInjectionService
{
    private readonly ILoggingService _logger = LoggingService.Instance;
    private readonly IClipboardService _clipboardService;
    private readonly IInputSimulationService _inputSimulationService;

    public ClipboardInjectionService()
        : this(new ClipboardService(), new SendInputSimulationService())
    {
    }

    public ClipboardInjectionService(
        IClipboardService clipboardService,
        IInputSimulationService inputSimulationService)
    {
        _clipboardService = clipboardService;
        _inputSimulationService = inputSimulationService;
    }

    public async Task<bool> InjectTextAsync(string text)
    {
        _logger.Debug($"InjectTextAsync called with text length: {text.Length}");

        if (string.IsNullOrEmpty(text))
        {
            _logger.Warning("InjectTextAsync: text is null or empty");
            return false;
        }

        var foregroundWindowBefore = GetForegroundWindowInfo();
        _logger.Debug($"Foreground window BEFORE injection: {foregroundWindowBefore}");
        if (foregroundWindowBefore.Handle == IntPtr.Zero)
        {
            _logger.Warning("No foreground window detected before injection");
            return false;
        }

        string? originalClipboard;
        try
        {
            originalClipboard = await RetryClipboardAsync("backup", ct => _clipboardService.GetTextAsync(ct));
            _logger.Debug($"Clipboard backed up, length: {originalClipboard?.Length ?? 0}");
        }
        catch (Exception ex)
        {
            _logger.Warning($"Clipboard backup failed: {ex.Message}");
            return false;
        }

        try
        {
            _logger.Info("Setting clipboard text...");
            await RetryClipboardAsync("set", ct => _clipboardService.SetTextAsync(text, ct));
            _logger.Debug("Clipboard text set successfully");

            await Task.Delay(100);

            var foregroundWindowAfterClipboard = GetForegroundWindowInfo();
            _logger.Debug($"Foreground window AFTER clipboard set: {foregroundWindowAfterClipboard}");
            if (foregroundWindowBefore.Handle != foregroundWindowAfterClipboard.Handle)
            {
                _logger.Warning("Foreground window changed before paste; aborting injection");
                return false;
            }

            _logger.Debug("Sending Ctrl+V using SendInput...");
            await _inputSimulationService.SendPasteShortcutAsync();
            _logger.Debug("Ctrl+V sent");

            await Task.Delay(200);

            var foregroundWindowAfterPaste = GetForegroundWindowInfo();
            _logger.Debug($"Foreground window AFTER Ctrl+V: {foregroundWindowAfterPaste}");
            if (foregroundWindowBefore.Handle != foregroundWindowAfterPaste.Handle)
            {
                _logger.Warning("Foreground window changed during paste");
            }

            _logger.Info("Text injection completed successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"InjectTextAsync failed: {ex.Message}", ex);
            return false;
        }
        finally
        {
            try
            {
                await _inputSimulationService.ReleaseModifierKeysAsync();
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to release modifier keys: {ex.Message}");
            }

            _ = RestoreClipboardAsync(text, originalClipboard);
        }
    }

    private async Task<T> RetryClipboardAsync<T>(
        string operationName,
        Func<CancellationToken, Task<T>> operation)
    {
        const int maxRetries = 3;

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                return await operation(cts.Token);
            }
            catch (Exception ex) when (attempt < maxRetries)
            {
                _logger.Warning($"Clipboard {operationName} failed, retry {attempt}/{maxRetries}: {ex.Message}");
                await Task.Delay(100);
            }
        }

        using var finalCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        return await operation(finalCts.Token);
    }

    private async Task RetryClipboardAsync(
        string operationName,
        Func<CancellationToken, Task> operation)
    {
        await RetryClipboardAsync(
            operationName,
            async cancellationToken =>
            {
                await operation(cancellationToken);
                return true;
            });
    }

    private async Task RestoreClipboardAsync(string injectedText, string? originalClipboard)
    {
        try
        {
            await Task.Delay(1500);
            var restored = await _clipboardService.RestoreTextIfUnchangedAsync(injectedText, originalClipboard);
            _logger.Info(restored
                ? "Original clipboard restored"
                : "Clipboard restore skipped because clipboard changed");
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to restore clipboard: {ex.Message}");
        }
    }

    private static ForegroundWindowInfo GetForegroundWindowInfo()
    {
        try
        {
            var hWnd = GetForegroundWindow();
            var title = new char[256];
            GetWindowText(hWnd, title, 256);
            var titleStr = new string(title).TrimEnd('\0');
            return new ForegroundWindowInfo(hWnd, titleStr);
        }
        catch (Exception ex)
        {
            return new ForegroundWindowInfo(IntPtr.Zero, $"Error getting window info: {ex.Message}");
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, char[] lpString, int nMaxCount);

    private readonly record struct ForegroundWindowInfo(IntPtr Handle, string Title)
    {
        public override string ToString() => $"hWnd=0x{Handle.ToInt64():X8}, title='{Title}'";
    }
}
