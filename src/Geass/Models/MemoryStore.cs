namespace Geass.Models;

public class MemoryStore
{
    public List<string> DifficultWords { get; set; } = [];
    public List<string> StylePreferences { get; set; } = [];
    public List<string> TranscriptionRules { get; set; } = [];
    public string UserDomain { get; set; } = "";
}
