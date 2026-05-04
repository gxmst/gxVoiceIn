namespace VoiceInputApp.Models;

public class ConversationResponse
{
    public string? Text { get; set; }
    public byte[]? AudioBytes { get; set; }
    public string? AudioMimeType { get; set; }
    public bool IsError { get; set; }
    public string? ErrorMessage { get; set; }
}
