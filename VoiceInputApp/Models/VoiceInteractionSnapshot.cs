namespace VoiceInputApp.Models;

public class VoiceInteractionSnapshot
{
    public InteractionMode Mode { get; set; }
    public string StateText { get; set; } = "空闲";
    public bool IsPlaying { get; set; }
    public string LastRecognizedText { get; set; } = string.Empty;
    public string LastAssistantReply { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public string TriggerKeyDisplay { get; set; } = string.Empty;
    public string LanguageDisplay { get; set; } = string.Empty;
    public string TtsVoiceDisplay { get; set; } = string.Empty;
    public string ConversationModelDisplay { get; set; } = string.Empty;
    public bool HasConversationHistory { get; set; }
    public int ConversationTurnCount { get; set; }
    public string? CurrentSessionId { get; set; }
    public string CurrentPlaybackState { get; set; } = "无";
    public bool IsRecording { get; set; }
    public bool IsThinking { get; set; }
    public bool IsSynthesizing { get; set; }
    public bool UseNativeAudio { get; set; }
}
