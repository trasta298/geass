using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Geass.Models;
using Geass.Services;

namespace Geass.ViewModels;

public partial class SettingsViewModel : ObservableObject, IDisposable
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
    private string _loadedFingerprint = "";
    private bool _isLoading;

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
    private string _styleKeyDisplay = "Tab";

    private Key _styleKey = Key.Tab;

    [ObservableProperty]
    private bool _isRecordingStyleKey;

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

    [ObservableProperty]
    private bool _hasUnsavedChanges;

    [ObservableProperty]
    private bool _isSaving;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _canEdit = true;

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private bool _hasValidationError;

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
            if (!isUpdating && !HasUnsavedChanges)
            {
                var memory = await _memoryService.LoadAsync();
                MemoryJson = JsonSerializer.Serialize(memory, DisplayJsonOptions);
                EstimatedTokens = _memoryService.EstimateTokens(memory);
                MarkClean();
            }
        });
    }

    public async Task LoadAsync()
    {
        _isLoading = true;
        var settings = await _settingsService.LoadAsync();
        ApiKey = settings.GeminiApiKey.Trim();
        SelectedTranscriptionModel = settings.TranscriptionModel;
        SelectedAnalysisModel = settings.AnalysisModel;
        Language = string.IsNullOrWhiteSpace(settings.Language) ? TranscriptionLanguages.Default : settings.Language.Trim();
        EnableScreenContext = settings.EnableScreenContext;

        _hotkeyKey = HotkeyService.ParseKey(settings.HotkeyKey);
        _hotkeyModifier = HotkeyService.ParseModifier(settings.HotkeyModifier);
        HotkeyDisplay = HotkeyService.FormatHotkey(_hotkeyModifier, _hotkeyKey);

        _styleKey = Enum.TryParse<Key>(settings.StyleKey, out var sk) ? sk : Key.Tab;
        StyleKeyDisplay = FormatKeyName(_styleKey);

        var memory = await _memoryService.LoadAsync();
        MemoryJson = JsonSerializer.Serialize(memory, DisplayJsonOptions);
        EstimatedTokens = _memoryService.EstimateTokens(memory);

        SaveButtonText = "Save";
        StatusMessage = "";
        HasValidationError = false;
        _isLoading = false;
        MarkClean();
        NotifyCommandStates();
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
        TrackUnsavedChanges();
    }

    public void SetStyleKey(Key key)
    {
        _styleKey = key;
        StyleKeyDisplay = FormatKeyName(key);
        IsRecordingStyleKey = false;
        TrackUnsavedChanges();
    }

    public static string FormatKeyName(Key key) => key switch
    {
        Key.OemTilde => "~",
        Key.OemMinus => "-",
        Key.OemPlus => "+",
        Key.OemOpenBrackets => "[",
        Key.OemCloseBrackets => "]",
        Key.OemBackslash or Key.Oem5 => "\\",
        Key.OemSemicolon or Key.Oem1 => ";",
        Key.OemQuotes or Key.Oem7 => "'",
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.Oem2 => "/",
        _ => key.ToString()
    };

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (IsBusy) return;

        if (!TryBuildSettingsForSave(out var settings, out var memory, out var validationMessage))
        {
            HasValidationError = true;
            StatusMessage = validationMessage;
            SaveButtonText = "Save";
            return;
        }

        IsSaving = true;
        SaveButtonText = "Saving...";
        StatusMessage = "";
        HasValidationError = false;

        try
        {
            await _settingsService.SaveAsync(settings);
            await _memoryService.SaveAsync(memory);

            SaveButtonText = "Saved!";
            IsSaved = true;
            HasValidationError = false;
            StatusMessage = "Settings saved";
            MarkClean();
            await Task.Delay(1200);
        }
        finally
        {
            SaveButtonText = "Save";
            IsSaved = false;
            IsSaving = false;
        }
    }

    private bool TryBuildSettingsForSave(out AppSettings settings, out MemoryStore memory, out string validationMessage)
    {
        settings = new AppSettings();
        memory = new MemoryStore();
        validationMessage = "";

        var normalizedApiKey = (ApiKey ?? "").Trim();
        var normalizedLanguage = string.IsNullOrWhiteSpace(Language) ? TranscriptionLanguages.Default : Language.Trim();

        if (string.IsNullOrWhiteSpace(normalizedApiKey))
        {
            validationMessage = "API key is required.";
            return false;
        }

        try
        {
            memory = JsonSerializer.Deserialize<MemoryStore>(MemoryJson) ?? new MemoryStore();
        }
        catch (JsonException)
        {
            validationMessage = "Memory JSON is invalid. Fix it before saving.";
            return false;
        }

        settings = new AppSettings
        {
            GeminiApiKey = normalizedApiKey,
            TranscriptionModel = SelectedTranscriptionModel,
            AnalysisModel = SelectedAnalysisModel,
            Language = normalizedLanguage,
            HotkeyKey = _hotkeyKey.ToString(),
            HotkeyModifier = _hotkeyModifier.ToString(),
            EnableScreenContext = EnableScreenContext,
            StyleKey = _styleKey.ToString()
        };

        // Keep normalized values in the UI after successful validation.
        ApiKey = normalizedApiKey;
        Language = normalizedLanguage;
        return true;
    }

    private bool CanSave()
    {
        return !IsBusy && HasUnsavedChanges;
    }

    [RelayCommand(CanExecute = nameof(CanRunMemoryActions))]
    private async Task OptimizeMemoryAsync()
    {
        if (IsBusy) return;

        IsMemoryUpdating = true;
        HasValidationError = false;
        StatusMessage = "";
        try
        {
            var memory = JsonSerializer.Deserialize<MemoryStore>(MemoryJson) ?? new MemoryStore();
            var optimized = await _geminiService.OptimizeMemoryAsync(memory);
            MemoryJson = JsonSerializer.Serialize(optimized, DisplayJsonOptions);
            EstimatedTokens = _memoryService.EstimateTokens(optimized);
        }
        catch (JsonException)
        {
            HasValidationError = true;
            StatusMessage = "Memory JSON is invalid. Fix it before optimizing.";
        }
        finally
        {
            IsMemoryUpdating = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunMemoryActions))]
    private void ClearMemory()
    {
        if (IsBusy) return;

        var empty = new MemoryStore();
        MemoryJson = JsonSerializer.Serialize(empty, DisplayJsonOptions);
        EstimatedTokens = _memoryService.EstimateTokens(empty);
        HasValidationError = false;
        StatusMessage = "";
    }

    private bool CanRunMemoryActions()
    {
        return !IsBusy;
    }

    private void TrackUnsavedChanges()
    {
        if (_isLoading) return;

        var hasChanges = BuildFingerprint() != _loadedFingerprint;
        if (HasUnsavedChanges != hasChanges)
        {
            HasUnsavedChanges = hasChanges;
        }
    }

    private void MarkClean()
    {
        _loadedFingerprint = BuildFingerprint();
        HasUnsavedChanges = false;
    }

    private string BuildFingerprint()
    {
        return string.Join("|",
            (ApiKey ?? "").Trim(),
            SelectedTranscriptionModel ?? "",
            SelectedAnalysisModel ?? "",
            (Language ?? "").Trim(),
            _hotkeyKey,
            _hotkeyModifier,
            EnableScreenContext,
            _styleKey,
            (MemoryJson ?? "").Trim());
    }

    private void UpdateBusyState()
    {
        IsBusy = IsSaving || IsMemoryUpdating;
        CanEdit = !IsBusy;
        NotifyCommandStates();
    }

    private void NotifyCommandStates()
    {
        SaveCommand.NotifyCanExecuteChanged();
        OptimizeMemoryCommand.NotifyCanExecuteChanged();
        ClearMemoryCommand.NotifyCanExecuteChanged();
    }

    partial void OnApiKeyChanged(string value)
    {
        TrackUnsavedChanges();
    }

    partial void OnSelectedTranscriptionModelChanged(string value)
    {
        TrackUnsavedChanges();
    }

    partial void OnSelectedAnalysisModelChanged(string value)
    {
        TrackUnsavedChanges();
    }

    partial void OnLanguageChanged(string value)
    {
        TrackUnsavedChanges();
    }

    partial void OnEnableScreenContextChanged(bool value)
    {
        TrackUnsavedChanges();
    }

    partial void OnHasUnsavedChangesChanged(bool value)
    {
        NotifyCommandStates();
    }

    partial void OnIsSavingChanged(bool value)
    {
        UpdateBusyState();
    }

    partial void OnIsMemoryUpdatingChanged(bool value)
    {
        UpdateBusyState();
    }

    public void Dispose()
    {
        _memoryService.IsUpdatingChanged -= OnMemoryUpdatingChanged;
    }

    partial void OnMemoryJsonChanged(string value)
    {
        TrackUnsavedChanges();

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
