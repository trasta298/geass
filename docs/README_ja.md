# 🔮 Geass

[English](../README.md)

Windows向けの音声文字起こしツール。ホットキーを押して話すだけで、文字起こしされたテキストがアクティブなアプリケーションに直接貼り付けられます。

Gemini API を使用しています。

## デモ

https://github.com/trasta298/geass/raw/main/docs/demo.mp4

## 特徴

- **ホットキー駆動** — `Alt+P` で録音開始、もう一度押すと文字起こし、結果は元のウィンドウに自動ペースト
- **ストリーミング文字起こし** — Gemini が処理する間、リアルタイムでテキストが表示される
- **スクリーンコンテキスト** — アクティブウィンドウのスクリーンショットを取得し、画面上の用語を認識して文字起こし精度を向上（オプション）
- **適応型メモリ** — ユーザーの修正から学習し、将来の文字起こしを改善
- **編集可能なプレビュー** — ペースト前にテキストを確認・編集可能
- **カスタマイズ** — ホットキー、Gemini モデル、言語などを設定画面から変更可能

## インストール

1. [Releases](https://github.com/trasta298/geass/releases) から `Geass-vX.X.X-win-x64.zip` をダウンロード
2. 展開して `Geass.exe` を実行
3. システムトレイアイコンを右クリック → **Settings** → [Gemini API キー](https://aistudio.google.com/apikey) を入力

## 使い方

| 操作 | 説明 |
|---|---|
| `Alt+P` | 録音開始 / 停止 |
| `Enter` | 録音停止 / 確定してペースト |
| `Esc` | キャンセル |

文字起こしオーバーレイは画面下部に表示されます。確定前にテキストを編集できます。

## ソースからビルド

[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) が必要です。

```bash
dotnet build src/Geass/Geass.csproj -c Debug
```

出力バイナリは `src/Geass/bin/Debug/net8.0-windows/Geass.exe` に生成されます。

## ライセンス

MIT
