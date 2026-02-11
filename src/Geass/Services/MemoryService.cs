using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Geass.Models;

namespace Geass.Services;

public partial class MemoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    private readonly string _memoryPath;
    private bool _isUpdating;

    public bool IsUpdating
    {
        get => _isUpdating;
        set
        {
            if (_isUpdating == value) return;
            _isUpdating = value;
            IsUpdatingChanged?.Invoke(value);
        }
    }

    public event Action<bool>? IsUpdatingChanged;

    public MemoryService()
    {
        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Geass");
        Directory.CreateDirectory(appDataDir);
        _memoryPath = Path.Combine(appDataDir, "memory.json");
    }

    public async Task<MemoryStore> LoadAsync()
    {
        if (!File.Exists(_memoryPath))
            return new MemoryStore();

        try
        {
            var json = await File.ReadAllTextAsync(_memoryPath);
            return JsonSerializer.Deserialize<MemoryStore>(json) ?? new MemoryStore();
        }
        catch
        {
            return new MemoryStore();
        }
    }

    public async Task SaveAsync(MemoryStore memory)
    {
        var json = JsonSerializer.Serialize(memory, JsonOptions);
        await File.WriteAllTextAsync(_memoryPath, json);
    }

    public int EstimateTokens(MemoryStore memory)
    {
        var json = JsonSerializer.Serialize(memory);
        int japaneseChars = 0;
        int asciiWordCount = 0;
        bool inAsciiWord = false;

        foreach (var ch in json)
        {
            if (IsJapaneseChar(ch))
            {
                japaneseChars++;
                inAsciiWord = false;
            }
            else if (char.IsLetterOrDigit(ch) && ch <= 0x7F)
            {
                if (!inAsciiWord)
                {
                    asciiWordCount++;
                    inAsciiWord = true;
                }
            }
            else
            {
                inAsciiWord = false;
            }
        }

        return (int)(japaneseChars * 1.5) + asciiWordCount;
    }

    public bool NeedsOptimization(MemoryStore memory)
    {
        return EstimateTokens(memory) > 500;
    }

    [GeneratedRegex(@"[\u3000-\u9FFF\uF900-\uFAFF]")]
    private static partial Regex JapaneseCharRegex();

    private static bool IsJapaneseChar(char ch)
    {
        return ch >= '\u3000' && ch <= '\u9FFF' || ch >= '\uF900' && ch <= '\uFAFF';
    }
}
