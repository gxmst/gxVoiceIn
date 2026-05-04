using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VoiceInputApp.Models;
using VoiceInputApp.Services.Settings;

namespace VoiceInputApp.Services.Conversation;

public class OpenAiConversationService : IConversationService
{
    private static readonly HttpClient SharedHttpClient = new() { Timeout = TimeSpan.FromSeconds(60) };
    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private readonly ISettingsService _settingsService;

    public OpenAiConversationService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_settingsService.Current.Llm.BaseUrl) &&
        !string.IsNullOrWhiteSpace(_settingsService.Current.Llm.ApiKey) &&
        !string.IsNullOrWhiteSpace(GetConversationModel());

    public async Task<ConversationResponse> SendAsync(ConversationRequest request, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return new ConversationResponse
            {
                IsError = true,
                ErrorMessage = "对话模型未配置完整"
            };
        }

        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, BuildUrl("/chat/completions"));
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settingsService.Current.Llm.ApiKey);

            var settings = _settingsService.Current;
            var useNativeAudio = settings.Llm.UseModelNativeAudio;

            var messages = new List<object>
            {
                new
                {
                    role = "system",
                    content = request.Language == Language.ZhCN
                        ? "你是一个中文语音助手。回答自然、简洁、口语化，优先直接回答用户问题。"
                        : "You are a voice assistant. Answer naturally and concisely."
                }
            };

            foreach (var item in request.History)
            {
                messages.Add(new { role = item.Role, content = item.Content });
            }

            messages.Add(new { role = "user", content = request.UserText });

            object body;

            if (useNativeAudio)
            {
                body = new
                {
                    model = GetConversationModel(),
                    messages,
                    modalities = new[] { "text", "audio" },
                    audio = new
                    {
                        voice = string.IsNullOrWhiteSpace(settings.Llm.TtsVoice) ? "alloy" : settings.Llm.TtsVoice,
                        format = "mp3"
                    },
                    temperature = 0.6,
                    max_tokens = 1200
                };
            }
            else
            {
                body = new
                {
                    model = GetConversationModel(),
                    messages,
                    temperature = 0.6,
                    max_tokens = 1200
                };
            }

            httpRequest.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            using var response = await SharedHttpClient.SendAsync(httpRequest, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new ConversationResponse
                {
                    IsError = true,
                    ErrorMessage = $"对话请求失败: HTTP {(int)response.StatusCode}"
                };
            }

            var result = JsonSerializer.Deserialize<ConversationCompletionResponse>(json, DeserializeOptions);
            var choice = result?.Choices?.FirstOrDefault();
            var message = choice?.Message;

            if (message == null)
            {
                return new ConversationResponse
                {
                    IsError = true,
                    ErrorMessage = "对话模型未返回内容"
                };
            }

            var text = (message.Content ?? string.Empty).Trim();

            byte[]? audioBytes = null;
            string? audioMimeType = null;

            if (useNativeAudio && message.Audio?.Data != null)
            {
                try
                {
                    audioBytes = Convert.FromBase64String(message.Audio.Data);
                    audioMimeType = "audio/mpeg";
                }
                catch (Exception)
                {
                    audioBytes = null;
                }
            }

            if (string.IsNullOrWhiteSpace(text) && audioBytes == null)
            {
                return new ConversationResponse
                {
                    IsError = true,
                    ErrorMessage = "对话模型未返回内容"
                };
            }

            return new ConversationResponse
            {
                Text = text,
                AudioBytes = audioBytes,
                AudioMimeType = audioMimeType
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ConversationResponse
            {
                IsError = true,
                ErrorMessage = $"对话请求异常: {ex.Message}"
            };
        }
    }

    private string GetConversationModel()
    {
        return string.IsNullOrWhiteSpace(_settingsService.Current.Llm.ConversationModel)
            ? _settingsService.Current.Llm.Model
            : _settingsService.Current.Llm.ConversationModel;
    }

    private string BuildUrl(string suffix)
    {
        return _settingsService.Current.Llm.BaseUrl.TrimEnd('/') + suffix;
    }

    private sealed class ConversationCompletionResponse
    {
        [JsonPropertyName("choices")]
        public List<ConversationChoice>? Choices { get; set; }
    }

    private sealed class ConversationChoice
    {
        [JsonPropertyName("message")]
        public ConversationMessageContent? Message { get; set; }
    }

    private sealed class ConversationMessageContent
    {
        [JsonPropertyName("role")]
        public string? Role { get; set; }

        [JsonPropertyName("content")]
        public string? Content { get; set; }

        [JsonPropertyName("audio")]
        public ConversationAudioContent? Audio { get; set; }
    }

    private sealed class ConversationAudioContent
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("data")]
        public string? Data { get; set; }

        [JsonPropertyName("expires_at")]
        public long? ExpiresAt { get; set; }

        [JsonPropertyName("transcript")]
        public string? Transcript { get; set; }
    }
}
