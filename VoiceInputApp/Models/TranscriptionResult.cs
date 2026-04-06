namespace VoiceInputApp.Models;

public class TranscriptionResult
{
    public string Text { get; set; } = string.Empty;
    public bool IsFinal { get; set; }
    public bool IsError { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
}
