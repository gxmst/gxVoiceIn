namespace VoiceInputApp.Services.Injection;

public interface ITextInjectionService
{
    Task<bool> InjectTextAsync(string text);
}
