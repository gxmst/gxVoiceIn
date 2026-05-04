namespace VoiceInputApp.Services.Audio;

public interface IAudioPlaybackService
{
    bool IsPlaying { get; }
    Task PlayAsync(byte[] audioData, string mimeType, CancellationToken cancellationToken);
    void Stop();
}
