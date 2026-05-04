namespace VoiceInputApp.Models;

public class TtsResult
{
    public byte[] AudioBytes { get; set; } = Array.Empty<byte>();
    public string MimeType { get; set; } = "audio/mpeg";
}
