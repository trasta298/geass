using CommunityToolkit.Mvvm.ComponentModel;

namespace Geass.ViewModels;

public partial class OverlayViewModel : ObservableObject
{
    [ObservableProperty]
    private string _statusText = "Listening...";

    [ObservableProperty]
    private bool _isRecording;

    [ObservableProperty]
    private bool _isProcessing;
}
