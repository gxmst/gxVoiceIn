using VoiceInputApp.Models;

namespace VoiceInputApp.Services.LLM;

public interface ILlmRefinementService
{
    Task<string> RefineAsync(string text, Language language);
    bool IsConfigured { get; }
}
