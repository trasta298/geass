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

    public async IAsyncEnumerable<string> TranscribeStyleInstructionAsync(
        string wavPath,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var googleAi = new GoogleAi(_apiKey);
        var model = googleAi.CreateGeminiModel(
            _transcriptionModel,
            systemInstruction: "You are a speech-to-text transcription engine. Output ONLY the verbatim transcription of the spoken words. Nothing else.");

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

    public async IAsyncEnumerable<string> ReformatStreamAsync(
        string text,
        string styleInstruction,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var googleAi = new GoogleAi(_apiKey);
        var model = googleAi.CreateGeminiModel(
            _transcriptionModel,
            systemInstruction: "Reformat the given text according to the user's style instruction. Output ONLY the reformatted text.");

        var prompt = $"## Text\n{text}\n\n## Style instruction\n{styleInstruction}";

        var request = new GenerateContentRequest();
        request.AddText(prompt, true, "user");
        request.GenerationConfig = new GenerationConfig
        {
            ThinkingConfig = BuildThinkingConfig(_transcriptionModel, ThinkingMode.Off)
        };

        await foreach (var response in model.StreamContentAsync(request, ct))
        {
            var chunk = response.Text();
            if (!string.IsNullOrEmpty(chunk))
                yield return chunk;
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
            Analyze the diff between a speech-to-text transcription and the user's corrected version.
            Extract learning data to improve future transcription accuracy.
            Focus ONLY on what changed — never comment on the content or meaning of the text.

            ## Original transcription
            {{original}}

            ## User-corrected text
            {{corrected}}

            ## Current memory
            {{JsonSerializer.Serialize(currentMemory, JsonOpts)}}

            ## Analysis steps
            1. Compare original and corrected text, listing all changes
            2. Classify each change:
               - **Misrecognition**: Speech was incorrectly recognized (e.g. "ぎゃっす" → "Geass") → DifficultWords
               - **Style/formatting**: Okurigana, punctuation, number formatting preferences → StylePreferences
               - **Generalizable rule**: A repeatable pattern, NOT a specific word fix → TranscriptionRules
               - **Domain signal**: Change suggests the user's field of expertise → UserDomain
               - **Content edit**: User rewrote the meaning (not a recognition error) → Ignore
            3. If ALL changes are content edits, return NoUpdate

            ## Output format (JSON only, no explanations)

            If only content edits or whitespace changes:
            {"NoUpdate": true}

            If there are learnable changes:
            {
              "DifficultWords": ["correct spelling of misrecognized words (preserve existing)"],
              "StylePreferences": ["user's formatting rules (preserve existing)"],
              "TranscriptionRules": ["generalizable rules (preserve existing, max 10)"],
              "UserDomain": "user's domain (preserve/supplement existing)"
            }

            ## TranscriptionRules criteria
            Only include rules that generalize across ANY context.
            GOOD examples:
            - "Prefer English original for tech terms (e.g. release, deploy, not リリース, デプロイ)"
            - "Use です・ます style consistently at end of sentences"
            - "Use kanji for number+counter expressions (10個, 3つ)"
            BAD examples (put these in DifficultWords instead):
            - "Change コンテキスト to codex" → specific word fix
            - "Convert katakana names to alphabet" → context-dependent, not generalizable

            ## Important
            - DifficultWords: record ONLY the correct spelling (e.g. "Geass", "WordPress")
            - Always preserve existing memory entries and append new ones
            - If TranscriptionRules exceeds 10 items, merge or drop the least important ones
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
            Optimize and compress the following memory data to fit within ~500 tokens.

            **Priority**: DifficultWords (misrecognized words) must be preserved first.
            Merge duplicates and similar entries. Remove outdated or overly generic information.

            ## Current memory
            {{JsonSerializer.Serialize(memory, JsonOpts)}}

            Return the following JSON format (no other text):
            {
              "DifficultWords": ["frequently misrecognized words/proper nouns only (highest priority)"],
              "StylePreferences": ["consolidated style rules"],
              "TranscriptionRules": ["generalizable transcription rules (max 10, merge duplicates)"],
              "UserDomain": "user's domain/field"
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

        if (memory.TranscriptionRules.Count > 0)
        {
            parts.Add($"Transcription rules (follow these): {string.Join("; ", memory.TranscriptionRules)}");
        }

        if (!string.IsNullOrWhiteSpace(memory.UserDomain))
        {
            parts.Add($"User's domain: {memory.UserDomain} — use this context to disambiguate homophones and technical terms.");
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
