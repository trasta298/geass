using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Extensions.DependencyInjection;
using Geass.Models;
using Geass.Services;
using Geass.ViewModels;
using Geass.Views;

namespace Geass;

public partial class App : Application
{
    private enum AppState { Idle, Recording, Streaming, Preview }

    private static Mutex? _mutex;
    private ServiceProvider? _serviceProvider;
    private TaskbarIcon? _trayIcon;

    private HotkeyService _hotkeyService = null!;
    private AudioCaptureService _audioCaptureService = null!;
    private MemoryService _memoryService = null!;
    private SettingsService _settingsService = null!;
    private ClipboardService _clipboardService = null!;

    private TranscriptionWindow? _transcriptionWindow;
    private CancellationTokenSource? _streamingCts;

    private AppState _state = AppState.Idle;
    private string _currentApiKey = "";
    private string _currentTranscriptionModel = GeminiModels.DefaultTranscription;
    private string _currentAnalysisModel = GeminiModels.DefaultAnalysis;
    private string _currentLanguage = TranscriptionLanguages.Default;
    private IntPtr _previousForegroundWindow;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(true, "Geass_SingleInstance", out var isNew);
        if (!isNew)
        {
            MessageBox.Show("Geass is already running.", "Geass", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        ConfigureServices();
        await LoadSettings();
        SetupTrayIcon();
        SetupHotkey();
    }

    private void ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<SettingsService>();
        services.AddSingleton<MemoryService>();
        services.AddSingleton<AudioCaptureService>();
        services.AddSingleton<ClipboardService>();
        services.AddSingleton<HotkeyService>();

        _serviceProvider = services.BuildServiceProvider();

        _settingsService = _serviceProvider.GetRequiredService<SettingsService>();
        _memoryService = _serviceProvider.GetRequiredService<MemoryService>();
        _audioCaptureService = _serviceProvider.GetRequiredService<AudioCaptureService>();
        _clipboardService = _serviceProvider.GetRequiredService<ClipboardService>();
        _hotkeyService = _serviceProvider.GetRequiredService<HotkeyService>();
    }

    private async Task LoadSettings()
    {
        var settings = await _settingsService.LoadAsync();
        _currentApiKey = settings.GeminiApiKey;
        _currentTranscriptionModel = settings.TranscriptionModel;
        _currentAnalysisModel = settings.AnalysisModel;
        _currentLanguage = settings.Language;

        var key = HotkeyService.ParseKey(settings.HotkeyKey);
        var modifier = HotkeyService.ParseModifier(settings.HotkeyModifier);
        _hotkeyService.Unregister();
        _hotkeyService.Register(key, modifier);
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "Geass - Voice Transcription",
            ContextMenu = new System.Windows.Controls.ContextMenu()
        };

        var settingsItem = new System.Windows.Controls.MenuItem { Header = "Settings" };
        settingsItem.Click += (_, _) => ShowSettings();

        var exitItem = new System.Windows.Controls.MenuItem { Header = "Exit" };
        exitItem.Click += (_, _) => ExitApplication();

        _trayIcon.ContextMenu.Items.Add(settingsItem);
        _trayIcon.ContextMenu.Items.Add(new System.Windows.Controls.Separator());
        _trayIcon.ContextMenu.Items.Add(exitItem);

        _trayIcon.Icon = CreateDefaultIcon();
    }

    private static System.Drawing.Icon CreateDefaultIcon()
    {
        using var bitmap = new System.Drawing.Bitmap(16, 16);
        using var g = System.Drawing.Graphics.FromImage(bitmap);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(System.Drawing.Color.Transparent);
        using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 107, 157));
        g.FillEllipse(brush, 1, 1, 14, 14);
        using var font = new System.Drawing.Font("Segoe UI", 7f, System.Drawing.FontStyle.Bold);
        using var textBrush = new System.Drawing.SolidBrush(System.Drawing.Color.White);
        var sf = new System.Drawing.StringFormat
        {
            Alignment = System.Drawing.StringAlignment.Center,
            LineAlignment = System.Drawing.StringAlignment.Center
        };
        g.DrawString("G", font, textBrush, new System.Drawing.RectangleF(0, 0, 16, 16), sf);
        return System.Drawing.Icon.FromHandle(bitmap.GetHicon());
    }

    private void SetupHotkey()
    {
        _hotkeyService.HotkeyPressed += OnHotkeyPressed;
    }

    private async void OnHotkeyPressed()
    {
        switch (_state)
        {
            case AppState.Idle:
                if (string.IsNullOrWhiteSpace(_currentApiKey))
                {
                    ShowSettings();
                    return;
                }
                StartRecording();
                break;

            case AppState.Recording:
                await StopRecordingAndTranscribe();
                break;

            case AppState.Streaming:
            case AppState.Preview:
                CancelCurrentOperation();
                break;
        }
    }

    private void StartRecording()
    {
        _state = AppState.Recording;
        _previousForegroundWindow = GetForegroundWindow();

        _transcriptionWindow = new TranscriptionWindow
        {
            OnStopRecording = async () => await StopRecordingAndTranscribe(),
            OnConfirm = async () => await ConfirmTranscription(),
            OnCancel = CancelCurrentOperation
        };

        _audioCaptureService.RecordingStopped += OnRecordingAutoStopped;
        _audioCaptureService.StartRecording();
        _transcriptionWindow.ShowRecording();
    }

    private async void OnRecordingAutoStopped()
    {
        await Dispatcher.InvokeAsync(async () => await StopRecordingAndTranscribe());
    }

    private async Task StopRecordingAndTranscribe()
    {
        if (_state != AppState.Recording)
            return;

        _state = AppState.Streaming;
        _audioCaptureService.RecordingStopped -= OnRecordingAutoStopped;

        _transcriptionWindow?.ShowProcessing();

        var wavPath = await _audioCaptureService.StopRecording();

        _transcriptionWindow?.ShowStreaming();

        _streamingCts = new CancellationTokenSource();
        _ = StreamTranscription(wavPath, _streamingCts.Token);
    }

    private async Task StreamTranscription(string wavPath, CancellationToken ct)
    {
        try
        {
            var memory = await _memoryService.LoadAsync();
            var gemini = new GeminiService(_currentApiKey, _currentTranscriptionModel, _currentAnalysisModel, _currentLanguage);

            var hasContent = false;
            await foreach (var chunk in gemini.TranscribeStreamAsync(wavPath, memory, ct))
            {
                hasContent = true;
                _transcriptionWindow?.AppendStreamingText(chunk);
            }

            await Dispatcher.InvokeAsync(() =>
            {
                var text = _transcriptionWindow?.TranscribedText?.Trim() ?? "";
                if (!hasContent || text.Length == 0)
                {
                    _state = AppState.Idle;
                    _transcriptionWindow?.Close();
                    _transcriptionWindow = null;
                    MessageBox.Show("音声を認識できませんでした。", "Geass",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                _transcriptionWindow?.ShowEditing();
                _state = AppState.Preview;
            });
        }
        catch (OperationCanceledException)
        {
            // Cancelled by user
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                _state = AppState.Idle;
                _transcriptionWindow?.Close();
                _transcriptionWindow = null;
                MessageBox.Show($"Transcription failed: {ex.Message}", "Geass",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            });
        }
        finally
        {
            try { File.Delete(wavPath); } catch { }
        }
    }

    private async Task ConfirmTranscription()
    {
        if (_transcriptionWindow is null)
            return;

        var transcribedText = _transcriptionWindow.TranscribedText;
        var originalText = _transcriptionWindow.OriginalText;

        // 1. Disable Topmost to prevent interference with focus transfer
        _transcriptionWindow.Topmost = false;

        // 2. While our window is still foreground, transfer focus to the target
        ForceRestoreForeground();

        // 3. Then hide our window (target already has focus)
        _transcriptionWindow.Hide();

        // 4. Wait for the target window to fully activate
        await Task.Delay(200);

        // 5. Clipboard + Ctrl+V
        await _clipboardService.SetTextAndPaste(transcribedText);

        // 6. Clean up
        _transcriptionWindow.Close();
        _transcriptionWindow = null;
        _state = AppState.Idle;

        if (originalText != transcribedText && !string.IsNullOrWhiteSpace(originalText))
        {
            _ = AnalyzeCorrectionInBackground(originalText, transcribedText);
        }
    }

    private async Task AnalyzeCorrectionInBackground(string original, string corrected)
    {
        _memoryService.IsUpdating = true;
        try
        {
            var memory = await _memoryService.LoadAsync();
            var gemini = new GeminiService(_currentApiKey, _currentTranscriptionModel, _currentAnalysisModel, _currentLanguage);
            var updatedMemory = await gemini.AnalyzeCorrectionAsync(original, corrected, memory);

            if (updatedMemory is null)
                return;

            await _memoryService.SaveAsync(updatedMemory);

            if (_memoryService.NeedsOptimization(updatedMemory))
            {
                var optimized = await gemini.OptimizeMemoryAsync(updatedMemory);
                await _memoryService.SaveAsync(optimized);
            }
        }
        catch
        {
            // Background task
        }
        finally
        {
            _memoryService.IsUpdating = false;
        }
    }

    private void CancelCurrentOperation()
    {
        _streamingCts?.Cancel();
        _streamingCts?.Dispose();
        _streamingCts = null;

        if (_state == AppState.Recording)
        {
            _audioCaptureService.RecordingStopped -= OnRecordingAutoStopped;
            _ = _audioCaptureService.StopRecording();
        }

        if (_transcriptionWindow is not null)
        {
            _transcriptionWindow.Topmost = false;
        }

        ForceRestoreForeground();

        _transcriptionWindow?.HideWithAnimation(() =>
        {
            _transcriptionWindow?.Close();
            _transcriptionWindow = null;
        });

        _state = AppState.Idle;
    }

    private void ShowSettings()
    {
        var vm = new SettingsViewModel(_settingsService, _memoryService, new GeminiService(_currentApiKey, _currentTranscriptionModel, _currentAnalysisModel, _currentLanguage), _hotkeyService);
        var window = new SettingsWindow(vm);
        window.Closed += async (_, _) =>
        {
            await LoadSettings();
        };
        window.ShowDialog();
    }

    private void ExitApplication()
    {
        CancelCurrentOperation();
        _hotkeyService.Dispose();
        _audioCaptureService.Dispose();
        _trayIcon?.Dispose();
        _serviceProvider?.Dispose();
        _mutex?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }

    private void ForceRestoreForeground()
    {
        if (_previousForegroundWindow == IntPtr.Zero) return;

        var targetThreadId = GetWindowThreadProcessId(_previousForegroundWindow, out _);
        var currentThreadId = GetCurrentThreadId();

        bool attached = false;
        if (targetThreadId != 0 && targetThreadId != currentThreadId)
        {
            attached = AttachThreadInput(currentThreadId, targetThreadId, true);
        }

        SetForegroundWindow(_previousForegroundWindow);

        if (attached)
        {
            AttachThreadInput(currentThreadId, targetThreadId, false);
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
