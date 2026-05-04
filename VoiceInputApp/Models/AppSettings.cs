namespace VoiceInputApp.Models;

public enum InteractionMode
{
    Input,
    Conversation,
    Hybrid
}

public class AppSettings
{
    public Language Language { get; set; } = Language.ZhCN;
    public InteractionMode Mode { get; set; } = InteractionMode.Input;
    public bool LlmEnabled { get; set; } = false;
    public int MicrophoneDeviceIndex { get; set; } = -1;
    public string MicrophoneDeviceName { get; set; } = string.Empty;
    public int TriggerKey { get; set; } = 0xA1;
    public int MaxConversationTurns { get; set; } = 6;
    public bool InterruptPlaybackOnHotkey { get; set; } = true;
    public bool AutoPlayResponseAudio { get; set; } = true;
    public LlmSettings Llm { get; set; } = new();
    public AsrSettings Asr { get; set; } = new();
}

public class LlmSettings
{
    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string ConversationModel { get; set; } = string.Empty;
    public string TtsModel { get; set; } = "tts-1";
    public string TtsVoice { get; set; } = "alloy";
    public bool UseModelNativeAudio { get; set; } = false;
}

public class AsrSettings
{
    public string AppId { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public string ResourceId { get; set; } = "volc.bigasr.sauc.duration";
    public string ModelName { get; set; } = "bigmodel";
    public bool EnableItn { get; set; } = true;
    public bool EnablePunc { get; set; } = true;
    public bool EnableDdc { get; set; } = true;
    public int EndWindowSize { get; set; } = 500;
    public string BoostingTableId { get; set; } = string.Empty;
}
