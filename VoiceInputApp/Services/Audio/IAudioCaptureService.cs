namespace VoiceInputApp.Services.Audio;

public class AudioLevelEventArgs : EventArgs
{
    public float Level { get; set; }
}

public interface IAudioCaptureService
{
    event EventHandler<AudioLevelEventArgs>? AudioLevelUpdated;
    event EventHandler<byte[]>? AudioDataAvailable;
    void StartCapture();
    void StopCapture();
    float GetCurrentLevel();
    bool IsCapturing { get; }
}
