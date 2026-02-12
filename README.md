# 🔮 Geass

[日本語](docs/README_ja.md)

Voice-to-text transcription tool for Windows. Press a hotkey, speak, and the transcribed text is pasted directly into your active application.

Powered by Gemini API.

https://github.com/user-attachments/assets/22b1cbd7-f248-4996-9b20-0c21d9001d26

## Features

- **Hotkey-driven workflow** — Press `Alt+P` to start recording, press again to transcribe, and the result is pasted into your previous window
- **Streaming transcription** — Text appears in real-time as Gemini processes your audio
- **Voice-driven style conversion** — After transcription, press `Tab` and speak a style instruction (e.g. "make it formal", "translate to English") to transform the text
- **Screen context** — Optionally captures a screenshot of your active window to improve transcription accuracy (e.g. recognizing on-screen terms)
- **Adaptive memory** — Learns from your corrections to improve future transcriptions
- **Editable preview** — Review and edit the transcription before pasting
- **Customizable** — Change hotkey, Gemini models, language, and more from the settings

## Installation

1. Download `Geass-vX.X.X-win-x64.zip` from [Releases](https://github.com/trasta298/geass/releases)
2. Extract and run `Geass.exe`
3. Right-click the system tray icon → **Settings** → enter your [Gemini API key](https://aistudio.google.com/apikey)

## Usage

### Basic workflow

1. Press `Alt+P` (default hotkey) to start recording
2. Speak, then press `Alt+P` or `Enter` to stop
3. Edit the transcription if needed
4. Press `Enter` to paste into the previous window, or `Esc` to cancel

During streaming or processing, press `Esc` or the hotkey to cancel.

### Keyboard shortcuts

| Key | During recording | During editing |
|---|---|---|
| Hotkey (`Alt+P`) | Stop recording | Cancel |
| `Enter` | Stop recording | Confirm and paste |
| `Esc` | Cancel | Cancel |
| `Tab` | Stop recording | Start style conversion |
| `Shift+Enter` | — | Insert newline |
| `Ctrl+Z` | — | Undo style conversion |

The hotkey and the style conversion trigger key (`Tab`) can be changed in Settings.

### Style conversion

While editing the transcribed text, press `Tab` to start a voice-driven style conversion. Speak an instruction such as "make it polite", "summarize", or "translate to English", then press `Enter`, `Tab`, or the hotkey to apply. The text will be rewritten according to your instruction. Press `Ctrl+Z` to revert.

## Build from Source

Requires [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
dotnet build src/Geass/Geass.csproj -c Debug
```

The output binary is at `src/Geass/bin/Debug/net8.0-windows/Geass.exe`.

## License

MIT
