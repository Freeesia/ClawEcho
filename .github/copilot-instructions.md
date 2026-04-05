# ClawEcho プロジェクトガイドライン

Raspberry Pi 向け半二重音声クライアント。ウェイクワード検出 → 録音 → STT → OpenClaw API → TTS → 再生のサイクルを実行する。詳細は [README.md](../README.md) および [dev.md](../dev.md) を参照。

## アーキテクチャ

4つの塊のみ。意図的に最小構成。

| 塊 | ファイル | 役割 |
|----|---------|------|
| CLI 入口 | `Commands.cs` | `daemon` / `oneshot *` サブコマンド定義 |
| 常駐処理 | `DaemonWorker.cs` | ウェイクワード待機 → VoiceSession ループ |
| 1会話処理 | `VoiceSession.cs` | アプリの中心。録音→STT→OpenClaw→TTS→再生の1サイクル |
| 外部接続 | `AudioIO.cs`, `WakeWordDetector.cs`, `WhisperStt.cs`, `OpenClawClient.cs` | 各コンポーネント |

設定クラスは `AppOptions.cs` に一元化（分割しない）。

## ビルドと実行

```bash
dotnet build
dotnet run -- daemon
dotnet run -- oneshot roundtrip
```

コマンド一覧は README.md 参照。

## 設計方針

- **抽象化は最小限**：インターフェースは差し替えが確定しているものだけ（現状 `ITtsClient` のみ）。`IAudioRecorder`・`IWakeWordDetector` 等は作らない
- **拡張性より追いやすさ**：まず動くこと・デバッグしやすいことを優先
- `daemon` と `oneshot` は**入口の違いだけ**。処理本体（VoiceSession）は共通

## 落とし穴・注意事項

### ConsoleAppFramework v5 の API 署名
標準の Generic Host API とは異なる。以下の署名を使うこと：

```csharp
// ConfigureServices — IConfiguration が第1引数
app.ConfigureServices((IConfiguration config, IServiceCollection services) => { ... });

// ConfigureDefaultConfiguration
app.ConfigureDefaultConfiguration((IConfigurationBuilder config) => { ... });

// ConfigureLogging
app.ConfigureLogging((IConfiguration config, ILoggingBuilder logging) => { ... });
```

### プラットフォーム依存
- `AudioIO.cs` は `arecord` / `aplay`（ALSA）に依存。**Linux/Raspberry Pi 専用**。Windows では動かない
- `WakeWordDetector.cs` は `python3 -m openwakeword` を子プロセスとして起動。Python + openWakeWord のインストールが必須

### 未実装コンポーネント
- **TTS**：`PlaceholderTtsClient` は何もしない。`TtsEndpoint` を設定しても現状は無効
- **無音検出**：`SilenceThreshold` は設定にあるが実装未完。録音は `MaxRecordSeconds` でタイムアウト

### Whisper モデル
- `WhisperModelPath` に ggml 形式モデルファイルのパスを指定。初回呼び出し時にモデルロード遅延あり
- `WhisperStt` は `SemaphoreSlim` でスレッドセーフ初期化を保護

### OpenClaw API
- エンドポイント：`POST /v1/responses`
- JSON シリアライズはソースジェネレーター（`OpenClawJsonContext`）を使用。型を追加する際は属性登録が必要
- 会話履歴はメモリ保持。`ClearHistory()` でリセット

## 環境変数

`OPENCLAW_App__<プロパティ名>` の形式で appsettings.json を上書き可能。例：`OPENCLAW_App__OpenClawBaseUrl=http://...`
