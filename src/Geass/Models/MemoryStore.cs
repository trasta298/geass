namespace Geass.Models;

public class MemoryStore
{
    public List<string> DifficultWords { get; set; } = [];
    public List<string> StylePreferences { get; set; } = [];
    public string CorrectionsSummary { get; set; } = "";
}
