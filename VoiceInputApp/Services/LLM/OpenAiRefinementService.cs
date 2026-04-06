using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using VoiceInputApp.Models;
using VoiceInputApp.Services.Settings;

namespace VoiceInputApp.Services.LLM;

public class OpenAiRefinementService : ILlmRefinementService
{
    private readonly ISettingsService _settingsService;
    private readonly HttpClient _httpClient;

    public bool IsConfigured =>
        !string.IsNullOrEmpty(_settingsService.Current.Llm.BaseUrl) &&
        !string.IsNullOrEmpty(_settingsService.Current.Llm.ApiKey) &&
        !string.IsNullOrEmpty(_settingsService.Current.Llm.Model);

    public OpenAiRefinementService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    public async Task<string> RefineAsync(string text, Language language)
    {
        if (!IsConfigured || string.IsNullOrEmpty(text)) return text;

        var settings = _settingsService.Current.Llm;
        var prompt = RefinementPrompt.GetPrompt(language);

        try
        {
            var requestBody = new
            {
                model = settings.Model,
                messages = new[]
                {
                    new { role = "system", content = prompt },
                    new { role = "user", content = text }
                },
                temperature = 0.1,
                max_tokens = 1000
            };

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {settings.ApiKey}");

            var url = settings.BaseUrl.TrimEnd('/') + "/chat/completions";
            var response = await _httpClient.PostAsJsonAsync(url, requestBody);

            if (!response.IsSuccessStatusCode) return text;

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<OpenAiResponse>(json);

            return result?.Choices?.FirstOrDefault()?.Message?.Content?.Trim() ?? text;
        }
        catch
        {
            return text;
        }
    }
}

internal class OpenAiResponse
{
    public List<OpenAiChoice>? Choices { get; set; }
}

internal class OpenAiChoice
{
    public OpenAiMessage? Message { get; set; }
}

internal class OpenAiMessage
{
    public string? Content { get; set; }
}
