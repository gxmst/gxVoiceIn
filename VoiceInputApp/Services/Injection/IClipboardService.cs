namespace VoiceInputApp.Services.Injection;

public interface IClipboardService
{
    Task<string?> GetTextAsync(CancellationToken cancellationToken = default);
    Task SetTextAsync(string text, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
    Task<bool> RestoreTextIfUnchangedAsync(string expectedCurrentText, string? restoreText, CancellationToken cancellationToken = default);
}
