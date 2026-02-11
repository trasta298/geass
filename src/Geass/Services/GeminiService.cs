using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using GenerativeAI;
using GenerativeAI.Types;
using Geass.Models;

namespace Geass.Services;

public class GeminiService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly string _apiKey;
    private readonly string _transcriptionModel;
    private readonly string _analysisModel;
    private readonly string _language;

    public GeminiService(
        string apiKey,
        string transcriptionModel = GeminiModels.DefaultTranscription,
        string analysisModel = GeminiModels.DefaultAnalysis,
        string language = TranscriptionLanguages.Default)
    {
        _apiKey = apiKey;
        _transcriptionModel = transcriptionModel;
        _analysisModel = analysisModel;
        _language = language;
    }

    public async IAsyncEnumerable<string> TranscribeStreamAsync(
        string wavPath,
        MemoryStore memory,
        string? screenDescription = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var googleAi = new GoogleAi(_apiKey);
        var model = googleAi.CreateGeminiModel(
            _transcriptionModel,
            systemInstruction: BuildSystemInstruction(memory, _language, screenDescription));

        var audioBytes = await File.ReadAllBytesAsync(wavPath, ct);
        var paddedAudio = AppendSilence(audioBytes, sampleRate: 16000, channels: 1, bitsPerSample: 16, durationMs: 500);
        var base64Audio = Convert.ToBase64String(paddedAudio);

        var request = new GenerateContentRequest();
        request.AddText("Transcribe.", true, "user");
        request.AddInlineData(base64Audio, "audio/wav", true, "user");
        request.GenerationConfig = new GenerationConfig
        {
            ThinkingConfig = BuildThinkingConfig(_transcriptionModel, ThinkingMode.Off)
        };

        await foreach (var response in model.StreamContentAsync(request, ct))
        {
            var text = response.Text();
            if (!string.IsNullOrEmpty(text))
                yield return text;
        }
    }

    public async Task<string?> DescribeScreenAsync(byte[] imageData, CancellationToken ct = default)
    {
        var googleAi = new GoogleAi(_apiKey);
        var model = googleAi.CreateGeminiModel(_transcriptionModel);

        var request = new GenerateContentRequest();
        var base64Image = Convert.ToBase64String(imageData);
        request.AddInlineData(base64Image, "image/jpeg", true, "user");
        request.AddText(
            "Extract all specific terms from this screenshot as a comma-separated list. " +
            "Include: keyboard shortcuts (e.g. Alt+P, Ctrl+C), proper nouns, product/service names, " +
            "file names, function names, variable names, URLs, technical terms, and UI labels. " +
            "Output ONLY the comma-separated list, nothing else. " +
            "Example output: Alt+P, Geass.csproj, Debug.WriteLine, SendInput, SCREEN CONTEXT",
            true, "user");
        request.GenerationConfig = new GenerationConfig
        {
            ThinkingConfig = BuildThinkingConfig(_transcriptionModel, ThinkingMode.Off)
        };

        var response = await model.GenerateContentAsync(request, ct);
        return response.Text();
    }

    public async Task<MemoryStore?> AnalyzeCorrectionAsync(
        string original,
        string corrected,
        MemoryStore currentMemory,
        CancellationToken ct = default)
    {
        if (original == corrected)
            return null;

        var googleAi = new GoogleAi(_apiKey);
        var model = googleAi.CreateGeminiModel(_analysisModel);

        var prompt = $$"""
            音声文字起こしの修正diffを分析してください。
            あなたの仕事は、元テキストと修正後テキストの**差分だけ**を見て、次回以降の音声認識精度を向上させるための学習データを抽出することです。
            テキストの内容や意味には一切言及せず、「何が変わったか」だけに集中してください。

            【元テキスト】
            {{original}}

            【修正後テキスト】
            {{corrected}}

            【現在のメモリ】
            {{JsonSerializer.Serialize(currentMemory, JsonOpts)}}

            ## 分析手順
            1. 元テキストと修正後テキストを1文字ずつ比較し、変更箇所をすべて列挙する
            2. 各変更箇所を以下のカテゴリに分類する:
               - **誤認識**: 音声が正しく認識されなかった（最重要。例:「ぎゃっす」→「Geass」）
               - **同音異義語**: 正しい読みだが漢字が違う（例:「公開」→「後悔」）
               - **表記ゆれ**: 送り仮名や表記の好み（例:「行なう」→「行う」）
               - **スタイル**: 句読点、数字表記、改行などのルール
               - **内容変更**: 発話内容自体の書き換え（認識ミスではない）
            3. 「内容変更」のみの場合はNoUpdateを返す

            ## 出力形式（JSONのみ、説明文不要）

            変更が「内容変更」のみ、または空白・改行のみの場合:
            {"NoUpdate": true}

            学習すべき変更がある場合:
            {
              "DifficultWords": ["誤認識された単語の正しい表記リスト（既存のものも保持）"],
              "StylePreferences": ["ユーザーの表記ルール（既存のものも保持）"],
              "CorrectionsSummary": "既存の要約を保持しつつ、今回の変更パターンを簡潔に追記"
            }

            ## 注意
            - DifficultWordsには正しい表記のみ記録する（例: "Geass", "WordPress"）
            - CorrectionsSummaryにはテキストの内容を書かないこと。変更パターンだけを記録する
              良い例: "「〜ください」を「〜下さい」に修正する傾向"
              悪い例: "ユーザーがコードの改善について話していた"
            - 既存のメモリ内容は必ず保持し、新しい学習内容を追加する形にする
            """;

        var request = new GenerateContentRequest();
        request.AddText(prompt, true, "user");
        request.GenerationConfig = new GenerationConfig
        {
            ThinkingConfig = BuildThinkingConfig(_analysisModel, ThinkingMode.High)
        };

        var response = await model.GenerateContentAsync(request, ct);
        var text = response.Text() ?? "";

        if (text.Contains("\"NoUpdate\""))
            return null;

        return ParseMemoryFromResponse(text, currentMemory);
    }

    public async Task<MemoryStore> OptimizeMemoryAsync(
        MemoryStore memory,
        CancellationToken ct = default)
    {
        var googleAi = new GoogleAi(_apiKey);
        var model = googleAi.CreateGeminiModel(_analysisModel);

        var prompt = $$"""
            以下のメモリデータを最適化し、約500トークン以内に圧縮してください。

            **重要**: DifficultWords（音声認識が誤認識した単語）は最優先で保持してください。
            重複や類似項目を統合し、古い情報や汎用的すぎる情報を削除してください。

            【現在のメモリ】
            {{JsonSerializer.Serialize(memory, JsonOpts)}}

            以下のJSON形式で返してください（他のテキストは不要）:
            {
              "DifficultWords": ["頻出する誤認識単語・固有名詞のみ（最重要）"],
              "StylePreferences": ["統合されたスタイル傾向"],
              "CorrectionsSummary": "圧縮された修正傾向の要約"
            }
            """;

        var response = await model.GenerateContentAsync(prompt, ct);
        var text = response.Text() ?? "";

        return ParseMemoryFromResponse(text, memory);
    }

    private static string BuildSystemInstruction(MemoryStore memory, string language, string? screenTerms = null)
    {
        var langHint = language == TranscriptionLanguages.Default
            ? "Auto-detect the language from the audio."
            : $"The audio language is {language}.";

        var parts = new List<string>
        {
            $"""
            You are a speech-to-text transcription engine.
            Output ONLY the verbatim transcription of the spoken words. Nothing else.

            Rules:
            - Never output timestamps, speaker labels, or annotations.
            - Never repeat or reference the user prompt or these instructions.
            - Never add explanations, commentary, or metadata.
            - If the audio contains no recognizable speech (silence, noise only), output nothing (empty response).
            - {langHint}
            """
        };

        if (!string.IsNullOrWhiteSpace(screenTerms))
        {
            parts.Add($"VOCABULARY HINT from user's screen - When the speaker refers to these terms, use the exact spelling shown here: {screenTerms}");
        }

        if (memory.DifficultWords.Count > 0)
        {
            parts.Add($"IMPORTANT - These words were previously misrecognized. Transcribe them accurately: {string.Join(", ", memory.DifficultWords)}");
        }

        if (memory.StylePreferences.Count > 0)
        {
            parts.Add($"User style preferences: {string.Join("; ", memory.StylePreferences)}");
        }

        if (!string.IsNullOrWhiteSpace(memory.CorrectionsSummary))
        {
            parts.Add($"Past correction patterns: {memory.CorrectionsSummary}");
        }

        return string.Join("\n\n", parts);
    }

    private enum ThinkingMode { Off, High }

    private static bool IsGemini3Pro(string model) => model.Contains("3-pro") || model.Contains("3.0-pro");
    private static bool IsGemini3(string model) => model.Contains("gemini-3");
    private static bool IsGemini25(string model) => model.Contains("gemini-2.5");
    private static bool SupportsThinking(string model) => IsGemini25(model) || IsGemini3(model);

    private static ThinkingConfig? BuildThinkingConfig(string model, ThinkingMode mode)
    {
        if (!SupportsThinking(model))
            return null;

        if (IsGemini3Pro(model))
        {
            // Gemini 3 Pro: cannot disable thinking, use thinkingLevel
            return new ThinkingConfig
            {
                ThinkingLevel = mode == ThinkingMode.High ? ThinkingLevel.HIGH : ThinkingLevel.LOW
            };
        }

        if (IsGemini3(model))
        {
            // Gemini 3 Flash: thinkingBudget supported for backward compat
            return new ThinkingConfig
            {
                ThinkingBudget = mode == ThinkingMode.High ? -1 : 0
            };
        }

        // Gemini 2.5 series: use thinkingBudget
        return new ThinkingConfig
        {
            ThinkingBudget = mode == ThinkingMode.High ? -1 : 0
        };
    }

    /// <summary>
    /// Appends silence to a WAV file's byte array by updating the header and adding zero samples.
    /// </summary>
    private static byte[] AppendSilence(byte[] wavBytes, int sampleRate, int channels, int bitsPerSample, int durationMs)
    {
        var bytesPerSample = bitsPerSample / 8;
        var silenceBytes = sampleRate * channels * bytesPerSample * durationMs / 1000;
        var result = new byte[wavBytes.Length + silenceBytes];
        Array.Copy(wavBytes, result, wavBytes.Length);

        // Update RIFF chunk size (offset 4, little-endian uint32): total file size - 8
        var riffSize = (uint)(result.Length - 8);
        result[4] = (byte)(riffSize & 0xFF);
        result[5] = (byte)((riffSize >> 8) & 0xFF);
        result[6] = (byte)((riffSize >> 16) & 0xFF);
        result[7] = (byte)((riffSize >> 24) & 0xFF);

        // Find "data" subchunk and update its size
        for (var i = 12; i < wavBytes.Length - 4; i++)
        {
            if (result[i] == 'd' && result[i + 1] == 'a' && result[i + 2] == 't' && result[i + 3] == 'a')
            {
                var dataSize = BitConverter.ToUInt32(result, i + 4) + (uint)silenceBytes;
                result[i + 4] = (byte)(dataSize & 0xFF);
                result[i + 5] = (byte)((dataSize >> 8) & 0xFF);
                result[i + 6] = (byte)((dataSize >> 16) & 0xFF);
                result[i + 7] = (byte)((dataSize >> 24) & 0xFF);
                break;
            }
        }

        return result;
    }

    private static MemoryStore ParseMemoryFromResponse(string text, MemoryStore fallback)
    {
        var jsonStart = text.IndexOf('{');
        var jsonEnd = text.LastIndexOf('}');

        if (jsonStart < 0 || jsonEnd < 0 || jsonEnd <= jsonStart)
            return fallback;

        var json = text[jsonStart..(jsonEnd + 1)];

        try
        {
            return JsonSerializer.Deserialize<MemoryStore>(json) ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }
}
