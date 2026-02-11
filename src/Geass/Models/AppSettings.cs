namespace Geass.Models;

public class AppSettings
{
    public string GeminiApiKey { get; set; } = "";
    public string TranscriptionModel { get; set; } = GeminiModels.DefaultTranscription;
    public string AnalysisModel { get; set; } = GeminiModels.DefaultAnalysis;
    public string Language { get; set; } = TranscriptionLanguages.Default;
    public string HotkeyKey { get; set; } = "P";
    public string HotkeyModifier { get; set; } = "Alt";
}

public static class TranscriptionLanguages
{
    public const string Default = "Auto";
}

public static class GeminiModels
{
    public const string DefaultTranscription = "gemini-2.5-flash-lite";
    public const string DefaultAnalysis = "gemini-3-flash-preview";

    public static readonly string[] Available =
    [
        "gemini-2.0-flash-lite",
        "gemini-2.5-flash-lite",
        "gemini-2.0-flash",
        "gemini-2.5-flash",
        "gemini-3-flash-preview",
        "gemini-3-pro-preview",
    ];
}
