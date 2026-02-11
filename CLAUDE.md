# Geass - Voice Transcription App

## Build & Run

This is a Windows WPF app (.NET 8) developed from WSL2. Use `cmd.exe` to build:

```bash
# Kill running instance first (exe locks the file)
cmd.exe /c "taskkill /F /IM Geass.exe 2>NUL"

# Build
cmd.exe /c "cd /d C:\Users\trasta\Documents\geass && dotnet build src\Geass\Geass.csproj -c Debug"

# Run
cmd.exe /c "start C:\Users\trasta\Documents\geass\src\Geass\bin\Debug\net8.0-windows\Geass.exe"
```

## Project Structure

```
src/Geass/
  App.xaml.cs              # Entry point, flow orchestration, hotkey/focus management
  Models/AppSettings.cs    # Settings model, GeminiModels list, TranscriptionLanguages
  Models/MemoryStore.cs    # Memory persistence model
  Services/
    GeminiService.cs       # Gemini API (streaming transcription, analysis, thinking config)
    AudioCaptureService.cs # Microphone capture (WAV)
    HotkeyService.cs       # Global hotkey registration (NHotkey.Wpf)
    ClipboardService.cs    # Clipboard + SendInput paste
    MemoryService.cs       # Memory store persistence + optimization
    SettingsService.cs     # AppSettings JSON persistence
  Helpers/
    KeyboardSimulator.cs   # SendInput P/Invoke (INPUT struct sizing critical on x64)
  ViewModels/              # CommunityToolkit.Mvvm ObservableObject/RelayCommand
  Views/                   # WPF windows (Overlay, Transcription, Preview, Settings)
  Resources/               # XAML styles, colors, animations
```

## Key Technical Notes

- **SendInput on x64**: `INPUTUNION` must include `MOUSEINPUT` field for correct struct size (40 bytes). Without it, `SendInput` silently fails.
- **Gemini Thinking Config**: Model-aware logic in `BuildThinkingConfig` — 2.0 has no thinking, 2.5/3-Flash use `thinkingBudget`, 3-Pro uses `thinkingLevel`.
- **Settings/Memory storage**: `%APPDATA%/Geass/` (not in repo).
