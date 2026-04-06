namespace VoiceInputApp.Models;

public enum Language
{
    ZhCN,
    EnUS,
    ZhTW,
    JaJP,
    KoKR
}

public static class LanguageExtensions
{
    public static string ToCode(this Language language)
    {
        return language switch
        {
            Language.ZhCN => "zh-CN",
            Language.EnUS => "en-US",
            Language.ZhTW => "zh-TW",
            Language.JaJP => "ja-JP",
            Language.KoKR => "ko-KR",
            _ => "zh-CN"
        };
    }

    public static string ToDisplayName(this Language language)
    {
        return language switch
        {
            Language.ZhCN => "简体中文",
            Language.EnUS => "English",
            Language.ZhTW => "繁體中文",
            Language.JaJP => "日本語",
            Language.KoKR => "한국어",
            _ => "简体中文"
        };
    }
}
