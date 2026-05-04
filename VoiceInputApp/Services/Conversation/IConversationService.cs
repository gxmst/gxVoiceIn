using VoiceInputApp.Models;

namespace VoiceInputApp.Services.Conversation;

public interface IConversationService
{
    bool IsConfigured { get; }
    Task<ConversationResponse> SendAsync(ConversationRequest request, CancellationToken cancellationToken);
}
