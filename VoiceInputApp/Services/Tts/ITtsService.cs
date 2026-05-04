using VoiceInputApp.Models;

namespace VoiceInputApp.Services.Tts;

public interface ITtsService
{
    bool IsConfigured { get; }
    Task<TtsResult> SynthesizeAsync(string text, Language language, CancellationToken cancellationToken);
}
