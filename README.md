# 🔮 Geass

[日本語](docs/README_ja.md)

Voice-to-text transcription tool for Windows. Press a hotkey, speak, and the transcribed text is pasted directly into your active application.

Powered by Gemini API.

## Features

- **Hotkey-driven workflow** — Press `Alt+P` to start recording, press again to transcribe, and the result is pasted into your previous window
- **Streaming transcription** — Text appears in real-time as Gemini processes your audio
- **Screen context** — Optionally captures a screenshot of your active window to improve transcription accuracy (e.g. recognizing on-screen terms)
- **Adaptive memory** — Learns from your corrections to improve future transcriptions
- **Editable preview** — Review and edit the transcription before pasting
- **Customizable** — Change hotkey, Gemini model, language, and more from the settings

## Installation

1. Download `Geass-vX.X.X-win-x64.zip` from [Releases](https://github.com/trasta298/geass/releases)
2. Extract and run `Geass.exe`
3. Right-click the system tray icon → **Settings** → enter your [Gemini API key](https://aistudio.google.com/apikey)

## Usage

| Action | Description |
|---|---|
| `Alt+P` | Start / stop recording |
| `Enter` | Stop recording / confirm and paste |
| `Esc` | Cancel |

The transcription overlay appears near the bottom of your screen. You can edit the text before confirming.

## Build from Source

Requires [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
dotnet build src/Geass/Geass.csproj -c Debug
```

The output binary is at `src/Geass/bin/Debug/net8.0-windows/Geass.exe`.

## License

MIT
