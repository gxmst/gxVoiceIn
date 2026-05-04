namespace VoiceInputApp.Models;

public class ConversationRequest
{
    public string UserText { get; set; } = string.Empty;
    public IReadOnlyList<ConversationMessage> History { get; set; } = Array.Empty<ConversationMessage>();
    public Language Language { get; set; }
}
