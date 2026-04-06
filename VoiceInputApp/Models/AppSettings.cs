namespace VoiceInputApp.Models;

public class AppSettings
{
    public Language Language { get; set; } = Language.ZhCN;
    public bool LlmEnabled { get; set; } = false;
    public int MicrophoneDeviceIndex { get; set; } = -1;
    public LlmSettings Llm { get; set; } = new();
    public AsrSettings Asr { get; set; } = new();
}

public class LlmSettings
{
    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
}

public class AsrSettings
{
    public string AppId { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public string ResourceId { get; set; } = "volc.bigasr.sauc.duration";
}
