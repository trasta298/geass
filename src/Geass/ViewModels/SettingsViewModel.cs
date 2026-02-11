using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Geass.Models;
using Geass.Services;

namespace Geass.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private static readonly JsonSerializerOptions DisplayJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly SettingsService _settingsService;
    private readonly MemoryService _memoryService;
    private readonly GeminiService _geminiService;
    private readonly HotkeyService _hotkeyService;

    [ObservableProperty]
    private string _apiKey = "";

    [ObservableProperty]
    private string _selectedTranscriptionModel = GeminiModels.DefaultTranscription;

    [ObservableProperty]
    private string _selectedAnalysisModel = GeminiModels.DefaultAnalysis;

    [ObservableProperty]
    private string _hotkeyDisplay = "Alt + P";

    private Key _hotkeyKey = Key.P;
    private ModifierKeys _hotkeyModifier = ModifierKeys.Alt;

    [ObservableProperty]
    private bool _isRecordingHotkey;

    [ObservableProperty]
    private string _language = TranscriptionLanguages.Default;

    [ObservableProperty]
    private string _memoryJson = "";

    [ObservableProperty]
    private int _estimatedTokens;

    [ObservableProperty]
    private bool _isMemoryUpdating;

    [ObservableProperty]
    private string _saveButtonText = "Save";

    [ObservableProperty]
    private bool _isSaved;

    [ObservableProperty]
    private bool _enableScreenContext;

    public string[] AvailableModels => GeminiModels.Available;

    public SettingsViewModel(SettingsService settingsService, MemoryService memoryService, GeminiService geminiService, HotkeyService hotkeyService)
    {
        _settingsService = settingsService;
        _memoryService = memoryService;
        _geminiService = geminiService;
        _hotkeyService = hotkeyService;

        IsMemoryUpdating = _memoryService.IsUpdating;
        _memoryService.IsUpdatingChanged += OnMemoryUpdatingChanged;
    }

    private void OnMemoryUpdatingChanged(bool isUpdating)
    {
        System.Windows.Application.Current?.Dispatcher.InvokeAsync(async () =>
        {
            IsMemoryUpdating = isUpdating;

            // Reload memory when background update finishes
            if (!isUpdating)
            {
                var memory = await _memoryService.LoadAsync();
                MemoryJson = JsonSerializer.Serialize(memory, DisplayJsonOptions);
                EstimatedTokens = _memoryService.EstimateTokens(memory);
            }
        });
    }

    public async Task LoadAsync()
    {
        var settings = await _settingsService.LoadAsync();
        ApiKey = settings.GeminiApiKey;
        SelectedTranscriptionModel = settings.TranscriptionModel;
        SelectedAnalysisModel = settings.AnalysisModel;
        Language = settings.Language;
        EnableScreenContext = settings.EnableScreenContext;

        _hotkeyKey = HotkeyService.ParseKey(settings.HotkeyKey);
        _hotkeyModifier = HotkeyService.ParseModifier(settings.HotkeyModifier);
        HotkeyDisplay = HotkeyService.FormatHotkey(_hotkeyModifier, _hotkeyKey);

        var memory = await _memoryService.LoadAsync();
        MemoryJson = JsonSerializer.Serialize(memory, DisplayJsonOptions);
        EstimatedTokens = _memoryService.EstimateTokens(memory);
    }

    partial void OnIsRecordingHotkeyChanged(bool value)
    {
        if (value)
            _hotkeyService.Unregister();
        else
            _hotkeyService.Register(HotkeyService.ParseKey(_hotkeyKey.ToString()), HotkeyService.ParseModifier(_hotkeyModifier.ToString()));
    }

    public void SetHotkey(Key key, ModifierKeys modifier)
    {
        _hotkeyKey = key;
        _hotkeyModifier = modifier;
        HotkeyDisplay = HotkeyService.FormatHotkey(modifier, key);
        IsRecordingHotkey = false;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var settings = new AppSettings
        {
            GeminiApiKey = ApiKey,
            TranscriptionModel = SelectedTranscriptionModel,
            AnalysisModel = SelectedAnalysisModel,
            Language = Language,
            HotkeyKey = _hotkeyKey.ToString(),
            HotkeyModifier = _hotkeyModifier.ToString(),
            EnableScreenContext = EnableScreenContext
        };
        await _settingsService.SaveAsync(settings);

        var memory = JsonSerializer.Deserialize<MemoryStore>(MemoryJson) ?? new MemoryStore();
        await _memoryService.SaveAsync(memory);

        SaveButtonText = "Saved!";
        IsSaved = true;
        await Task.Delay(1500);
        SaveButtonText = "Save";
        IsSaved = false;
    }

    [RelayCommand]
    private async Task OptimizeMemoryAsync()
    {
        IsMemoryUpdating = true;
        try
        {
            var memory = JsonSerializer.Deserialize<MemoryStore>(MemoryJson) ?? new MemoryStore();
            var optimized = await _geminiService.OptimizeMemoryAsync(memory);
            MemoryJson = JsonSerializer.Serialize(optimized, DisplayJsonOptions);
            EstimatedTokens = _memoryService.EstimateTokens(optimized);
        }
        finally
        {
            IsMemoryUpdating = false;
        }
    }

    [RelayCommand]
    private void ClearMemory()
    {
        var empty = new MemoryStore();
        MemoryJson = JsonSerializer.Serialize(empty, DisplayJsonOptions);
        EstimatedTokens = _memoryService.EstimateTokens(empty);
    }

    partial void OnMemoryJsonChanged(string value)
    {
        try
        {
            var memory = JsonSerializer.Deserialize<MemoryStore>(value);
            if (memory is not null)
            {
                EstimatedTokens = _memoryService.EstimateTokens(memory);
            }
        }
        catch (JsonException)
        {
            // Invalid JSON, keep current token estimate
        }
    }
}
