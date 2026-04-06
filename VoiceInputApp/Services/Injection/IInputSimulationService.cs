namespace VoiceInputApp.Services.Injection;

public interface IInputSimulationService
{
    Task SendPasteShortcutAsync(CancellationToken cancellationToken = default);
    Task ReleaseModifierKeysAsync(CancellationToken cancellationToken = default);
}
