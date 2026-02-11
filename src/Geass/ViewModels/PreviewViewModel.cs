using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Geass.ViewModels;

public partial class PreviewViewModel : ObservableObject
{
    [ObservableProperty]
    private string _transcribedText = "";

    [ObservableProperty]
    private bool _isStreaming = true;

    public string OriginalText { get; set; } = "";

    public Action? OnConfirm { get; set; }
    public Action? OnCancel { get; set; }

    public void AppendText(string chunk)
    {
        TranscribedText += chunk;
    }

    [RelayCommand]
    private void Confirm()
    {
        OnConfirm?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        OnCancel?.Invoke();
    }
}
