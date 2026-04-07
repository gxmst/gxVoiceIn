using VoiceInputApp.Models;

namespace VoiceInputApp.Services.Transcription;

public interface ITranscriptionService
{
    Task StartStreamingRecognitionAsync(
        Language language,
        string sessionId,
        IReadOnlyList<string>? contextTexts,
        Action<TranscriptionResult> onResult,
        CancellationToken cancellationToken);

    Task SendAudioDataAsync(byte[] data, CancellationToken cancellationToken);
    Task<TranscriptionResult?> StopRecognitionAsync(CancellationToken cancellationToken);
    bool IsConfigured { get; }
}
