using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using VoiceInputApp.Models;
using VoiceInputApp.Services.Settings;

namespace VoiceInputApp.Services.Tts;

public class OpenAiTtsService : ITtsService
{
    private static readonly HttpClient SharedHttpClient = new() { Timeout = TimeSpan.FromSeconds(60) };
    private readonly ISettingsService _settingsService;

    public OpenAiTtsService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_settingsService.Current.Llm.BaseUrl) &&
        !string.IsNullOrWhiteSpace(_settingsService.Current.Llm.ApiKey) &&
        !string.IsNullOrWhiteSpace(_settingsService.Current.Llm.TtsModel);

    public async Task<TtsResult> SynthesizeAsync(string text, Language language, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("TTS 未配置完整");
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, BuildUrl("/audio/speech"));
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settingsService.Current.Llm.ApiKey);
        var body = new
        {
            model = _settingsService.Current.Llm.TtsModel,
            voice = string.IsNullOrWhiteSpace(_settingsService.Current.Llm.TtsVoice) ? "alloy" : _settingsService.Current.Llm.TtsVoice,
            input = text,
            format = "mp3"
        };

        httpRequest.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await SharedHttpClient.SendAsync(httpRequest, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"TTS 请求失败: HTTP {(int)response.StatusCode}");
        }

        return new TtsResult
        {
            AudioBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken),
            MimeType = response.Content.Headers.ContentType?.MediaType ?? "audio/mpeg"
        };
    }

    private string BuildUrl(string suffix)
    {
        return _settingsService.Current.Llm.BaseUrl.TrimEnd('/') + suffix;
    }
}
