# ClawEcho

OpenClaw と会話するための半二重音声クライアントです。

Raspberry Pi / Linux で常駐するスマートスピーカー用途を主な対象にしつつ、Windows でも NAudio/WASAPI による録音・再生の検証ができる構成です。

## 実装状態

- CLI: `daemon` と `oneshot` 系コマンドを実装済み
- 音声 I/O:
  - Linux: ALSA の `arecord` / `aplay`
  - Windows: NAudio の WASAPI 共有モード
- STT: Whisper.net
  - `WhisperModelPath` 未指定時は `WhisperModelType` に応じて ggml モデルを自動ダウンロード
- OpenClaw 連携: OpenAI SDK の Responses API クライアントで `/v1/responses` に接続
  - `PreviousResponseId` をメモリに保持して会話を継続
- Wake word: `python3 -m openwakeword` をサブプロセスとして起動し、標準出力の `WAKE` を検出
- TTS: 現状は `ConsoleTtsClient`
  - 応答テキストを標準出力に表示するだけで、音声合成はまだ行わない

未実装または制限事項は [todo.md](todo.md) を参照してください。

## 必要条件

- .NET 10 SDK または Runtime
- OpenClaw サーバー
- OpenClaw の Bearer token
- Linux / Raspberry Pi で録音・再生する場合:
  - ALSA (`arecord`, `aplay`)
  - Python と openWakeWord
- Windows で録音・再生する場合:
  - `net10.0-windows` ターゲットでビルド
  - Wake word 検出はまだ Windows 向けに調整されていません

## 設定

設定は次の順で読み込まれ、後の値が前の値を上書きします。

1. アプリに同梱された `appsettings.json`
2. ユーザー設定ディレクトリの `appsettings.json`
3. ユーザー設定ディレクトリの `appsettings.Local.json`
4. `OPENCLAW_` プレフィックスの環境変数

ユーザー設定ディレクトリは以下です。

- Linux: `~/.config/claw-echo/`
- Windows: `%APPDATA%\claw-echo\`

設定例:

```json
{
  "App": {
    "OpenClawBaseUrl": "http://your-openclaw-server:8080",
    "OpenClawBearerToken": "your-token",
    "OpenClawModel": "openclaw:main",
    "SessionUser": "clawecho",
    "InputDevice": "default",
    "OutputDevice": "default",
    "MaxRecordSeconds": 15,
    "WhisperModelType": "Base",
    "WhisperLanguage": "ja",
    "WakeWordModelPath": "/path/to/wake_word.onnx",
    "WakeWordThreshold": 0.5
  }
}
```

環境変数で上書きする場合:

```bash
OPENCLAW_App__OpenClawBaseUrl=http://localhost:8080
OPENCLAW_App__OpenClawBearerToken=your-token
```

## 使い方

開発中はプロジェクトを指定して実行します。

```bash
dotnet run --project src/OpenClawVoiceClient -- daemon
dotnet run --project src/OpenClawVoiceClient -- oneshot roundtrip
```

dotnet tool としてインストール済みの場合は `clawecho` コマンドを使います。

```bash
clawecho daemon
clawecho oneshot roundtrip
```

### 常駐モード

```bash
clawecho daemon
```

ウェイクワードを待ち、検出したら `VoiceSession` を 1 回実行し、また待機に戻ります。

### 単発モード

```bash
# 最大録音時間までマイクから録音
clawecho oneshot record

# WAV ファイルを文字起こし
clawecho oneshot transcribe /path/to/audio.wav

# テキストを OpenClaw に送信
clawecho oneshot ask "こんにちは"

# 録音 -> STT -> OpenClaw -> TTS -> 再生の 1 サイクル
clawecho oneshot roundtrip

# ウェイクワード検出テスト
clawecho oneshot wake-test

# テキストを TTS に渡す
clawecho oneshot speak "こんにちは"
```

現状の TTS は標準出力のみなので、`roundtrip` と `speak` は応答テキストの表示までです。

## systemd サービス

Linux では dotnet tool としてインストールした後、次のコマンドで systemd サービスを登録できます。

```bash
sudo clawecho install
```

このコマンドは以下を行います。

- `~/.config/claw-echo/` を作成
- 設定テンプレートを `appsettings.json` としてコピー
- `/etc/systemd/system/claw-echo.service` を生成
- `systemctl daemon-reload` を実行
- 既定では `claw-echo.service` を自動起動に登録

サービスを開始するには:

```bash
sudo systemctl start claw-echo.service
```

登録解除:

```bash
sudo clawecho uninstall
```

## 開発

ビルド:

```bash
dotnet build src/ClawEcho.sln
```

ターゲットフレームワーク:

- `net10.0`
- `net10.0-windows`
