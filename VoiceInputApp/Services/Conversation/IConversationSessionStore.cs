using VoiceInputApp.Models;

namespace VoiceInputApp.Services.Conversation;

public interface IConversationSessionStore
{
    IReadOnlyList<ConversationMessage> GetMessages();
    void AddUserMessage(string text);
    void AddAssistantMessage(string text);
    void Clear();
}
