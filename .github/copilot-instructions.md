# ClawEcho プロジェクトガイドライン

ClawEcho は OpenClaw と会話する半二重音声クライアントです。主用途は Raspberry Pi / Linux 上の常駐スマートスピーカーですが、Windows でも録音・再生の検証ができるように multi-TFM で構成しています。

詳細な利用手順は [README.md](../README.md)、設計背景は [dev.md](../dev.md)、残タスクは [todo.md](../todo.md) を参照してください。

## 現在の実装状態

- CLI は ConsoleAppFramework v5
- ターゲットは `net10.0` と `net10.0-windows`
- Linux 音声 I/O は `arecord` / `aplay`
- Windows 音声 I/O は NAudio / WASAPI 共有モード
- STT は Whisper.net
- OpenClaw 連携は OpenAI SDK の Responses API
- Wake word は `python3 -m openwakeword` サブプロセス
- TTS はまだ実音声合成なし
  - DI 登録は `ITtsClient -> ConsoleTtsClient`
  - 応答テキストを標準出力に表示し、音声ファイルは返さない

## アーキテクチャ

意図的に小さい構成を維持します。新しい層や抽象を追加する前に、既存の塊に収まるかを確認してください。

| 塊 | 主なファイル | 役割 |
|----|-------------|------|
| 起動・DI | `Program.cs` | 設定、DI、ログ、ConsoleAppFramework 起動 |
| CLI | `Commands.cs`, `SystemdCommands.cs` | `daemon` / `oneshot *` / `install` / `uninstall` |
| 常駐処理 | `DaemonWorker.cs` | ウェイクワード待機 -> `VoiceSession` 実行のループ |
| 1 会話処理 | `VoiceSession.cs` | 録音 -> STT -> OpenClaw -> TTS -> 再生 |
| 音声 I/O | `IAudioIO.cs`, `AudioIO.cs`, `WindowsAudioIO.cs` | Linux / Windows の録音・再生 |
| 外部接続 | `WakeWordDetector.cs`, `WhisperStt.cs`, `OpenClawClient.cs` | Wake word、STT、OpenClaw API |
| TTS | `ITtsClient.cs`, `ConsoleTtsClient.cs`, `PlaceholderTtsClient.cs` | TTS 差し替え口と現状のデバッグ出力 |
| 設定 | `AppOptions.cs`, `appsettings.json` | アプリ設定 |

## 設計方針

- **抽象化は最小限**にする
  - 現状の明示的な差し替え口は `IAudioIO` と `ITtsClient`
  - `IWakeWordDetector`、`ISttEngine`、`IOpenClawClient` などは、必要になるまで追加しない
- `daemon` と `oneshot` は入口だけを分ける
  - 1 会話の本体は `VoiceSession` に集約する
- 設定は `AppOptions.cs` に集約する
  - 小さな Options 型へ先回りして分割しない
- 外部プロセス起動の共通化は急がない
  - 同じ複雑さが複数箇所に出てから考える
- テストや実装上の必要がない helper / wrapper は増やさない

## ビルドと実行

```bash
dotnet build src/ClawEcho.sln
dotnet run --project src/OpenClawVoiceClient -- daemon
dotnet run --project src/OpenClawVoiceClient -- oneshot roundtrip
```

dotnet tool としてインストール済みの場合:

```bash
clawecho daemon
clawecho oneshot ask "こんにちは"
```

## 設定読み込み

`Program.cs` では以下の順に設定を読みます。後勝ちです。

1. `AppContext.BaseDirectory/appsettings.json`
2. ユーザー設定ディレクトリの `appsettings.json`
3. ユーザー設定ディレクトリの `appsettings.Local.json`
4. `OPENCLAW_` プレフィックスの環境変数

ユーザー設定ディレクトリ:

- Linux: `~/.config/claw-echo/`
- Windows: `%APPDATA%\claw-echo\`

環境変数は `OPENCLAW_App__OpenClawBaseUrl=http://...` の形式です。

## 注意事項

### ConsoleAppFramework v5

標準の Generic Host API と同じ感覚で書かないこと。既存の `Program.cs` と同じ形に合わせます。

```csharp
app.ConfigureDefaultConfiguration(config => { ... });
app.ConfigureServices((config, services) => { ... });
app.ConfigureLogging((config, logging) => { ... });
```

### 録音終了判定

`IAudioIO.RecordUntilSilenceAsync` という名前ですが、現状は実際の無音検出ではありません。

- Linux: `arecord` を `MaxRecordSeconds` でキャンセル
- Windows: WASAPI 録音を `MaxRecordSeconds` で停止し、Whisper 用にリサンプル
- `SilenceThreshold` と `SilenceDurationMs` はまだ実装に使われていません

この挙動を説明するドキュメントや TODO では、無音検出済みと書かないでください。

### Wake word

`WakeWordDetector` は現在 `python3 -m openwakeword` 固定です。

- 標準出力に `WAKE` を含む行が出たら検出扱い
- Windows 向けの `python` / `py` 切り替えは未実装
- モデルパスやデバイス名に空白がある場合の引数安全性も未整理

### TTS

実際の音声合成は未実装です。

- `ConsoleTtsClient` は応答テキストを `Console.WriteLine` して `null` を返す
- `TtsEndpoint` は設定に存在するが、現状は使われていない
- `oneshot speak` と `roundtrip` は音声再生まで進まない

### OpenClaw API

- `OpenClawClient` は OpenAI SDK の `ResponsesClient` を使う
- `OpenClawBaseUrl` を SDK の `Endpoint` として指定する
- `OpenClawBearerToken` を API key credential として使う
- `PreviousResponseId` をメモリに保持して会話を継続する
- `ClearHistory()` でセッションをリセットする

### systemd

Linux の systemd 登録は `SystemdCommands.cs` の `install` / `uninstall` で行います。

- `sudo clawecho install`
- `sudo clawecho uninstall`
- サービス名は `claw-echo.service`
- サービスファイルは `/etc/systemd/system/claw-echo.service`

古い `deploy/*.service` 前提の手順を復活させないでください。
