# OpenClaw + Raspberry Pi 音声クライアント 実装アーキテクチャ

## 目的

Raspberry Pi 上で動作する半二重の音声クライアントを、**C# / .NET 10 / Generic Host / ConsoleAppFramework** でシンプルに実装する。

この段階では、拡張性よりも **まず動くこと・追いやすいこと・デバッグしやすいこと** を優先する。

---

# 前提

- 言語: C#
- ランタイム: .NET 10
- ホスト: Generic Host + Microsoft.Extensions.Hosting.Systemd
- CLI: ConsoleAppFramework
- STT: Whisper.net
- TTS: 保留（API 呼び出しのプレースホルダー）
- 通信先: OpenClaw のみ
- OpenClaw 連携: `/v1/responses`
- 会話方式: 半二重
- モード: `daemon` と `oneshot`

---

# 結論

今はレイヤーを細かく分けすぎない。

**以下の最小構成で始める。**

- `Program.cs`
- `Commands.cs`
- `DaemonWorker.cs`
- `VoiceSession.cs`
- `AudioIO.cs`
- `WakeWordDetector.cs`
- `WhisperStt.cs`
- `OpenClawClient.cs`
- `TtsClient.cs`
- `AppOptions.cs`

つまり、**「CLI」「常駐処理」「1会話処理」「外部接続」** の4塊だけにする。

---

# 設計方針

## 1. 抽象化は最小限にする

最初から interface を大量に作らない。

この時点で interface を作るのは、**差し替え予定が確定しているものだけ** に絞る。

### 作ってよいもの

- `ITtsClient`

### まだ作らないもの

- `IAudioRecorder`
- `IAudioPlayer`
- `IWakeWordDetector`
- `ISttEngine`
- `IOpenClawClient`

理由:

- これらは当面実装が1つに決まっている
- 先に抽象化してもメリットより読みづらさが勝つ

---

## 2. 1発話の処理を `VoiceSession` に集約する

音声クライアントの本体は、結局これだけ。

1. 録音する
2. STT する
3. OpenClaw に送る
4. TTS を呼ぶ
5. 再生する

この1サイクルを `` にまとめる。

`VoiceSession` がアプリの中心。

---

## 3. `daemon` と `oneshot` は入口だけ変える

処理本体は分けない。

- `daemon`: wake word を待って `VoiceSession.RunFromMicAsync()` を繰り返す
- `oneshot`: コマンドに応じて `VoiceSession` や各コンポーネントを単発実行する

つまり、**常駐か単発かは入口の違いでしかない** とみなす。

---

## 4. 音声 I/O は 1 クラスにまとめる

録音と再生を分けすぎない。

- `AudioIO.StartRecordingAsync()`
- `AudioIO.RecordUntilSilenceAsync()`
- `AudioIO.PlayAsync()`

最初はこれで十分。

ALSA を `arecord` / `aplay` で叩く責務もここにまとめる。

---

## 5. 状態機械は class 化しない

今の段階では `TerminalStateMachine` のような専用型は不要。

`VoiceSession` 内でログを出しながら順番に処理すれば足りる。

例:

- `WakeDetected`
- `Recording`
- `Transcribing`
- `CallingOpenClaw`
- `Speaking`

これは enum かログ文字列で十分。

---

# 最小プロジェクト構成

```text
src/
  OpenClawVoiceClient/
    Program.cs
    Commands.cs
    DaemonWorker.cs
    VoiceSession.cs
    AudioIO.cs
    WakeWordDetector.cs
    WhisperStt.cs
    OpenClawClient.cs
    ITtsClient.cs
    PlaceholderTtsClient.cs
    AppOptions.cs
```

これ以上は増やさない。

---

# 各ファイルの責務

## Program.cs

- Host 作成
- 設定読み込み
- DI 登録
- ConsoleAppFramework 起動

## Commands.cs

- `daemon`
- `oneshot record`
- `oneshot transcribe`
- `oneshot ask`
- `oneshot roundtrip`
- `oneshot wake-test`
- `oneshot speak`

CLI の入口だけ持つ。

## DaemonWorker.cs

- wake word を待つ
- 検出したら `VoiceSession.RunFromMicAsync()` を呼ぶ
- 失敗してもループ継続

## VoiceSession.cs

- 1発話分の本体処理
- 録音
- STT
- OpenClaw 送信
- TTS
- 再生

**アプリの中心。**

## AudioIO.cs

- `arecord` 呼び出し
- 無音まで録音
- `aplay` 呼び出し
- 一時 wav ファイル管理

## WakeWordDetector.cs

- wake word 待機
- 検出を返す

## WhisperStt.cs

- Whisper.net で wav を文字起こし

## OpenClawClient.cs

- `/v1/responses` 呼び出し
- テキスト応答を返す

## ITtsClient / PlaceholderTtsClient

- TTS だけは未確定なのでここだけ抽象化
- 今はプレースホルダー

## AppOptions.cs

- 必要な設定を1ファイルにまとめる

---

# AppOptions の方針

設定クラスも分けすぎない。

最初は1つでよい。

```csharp
public sealed class AppOptions
{
    public string OpenClawBaseUrl { get; set; } = "";
    public string OpenClawBearerToken { get; set; } = "";
    public string InputDevice { get; set; } = "default";
    public string OutputDevice { get; set; } = "default";
    public int SampleRate { get; set; } = 16000;
    public int Channels { get; set; } = 1;
    public int MaxRecordSeconds { get; set; } = 15;
    public int SilenceDurationMs { get; set; } = 1200;
    public double SilenceThreshold { get; set; } = 0.01;
    public string WhisperModelPath { get; set; } = "";
    public string WhisperLanguage { get; set; } = "ja";
    public string WakeWordModelPath { get; set; } = "";
    public float WakeWordThreshold { get; set; } = 0.5f;
    public string? TtsEndpoint { get; set; }
}
```

必要になってから分割する。

---

# DI 方針

DI も最小限でよい。

登録するもの:

- `AudioIO`
- `WakeWordDetector`
- `WhisperStt`
- `OpenClawClient`
- `ITtsClient` → `PlaceholderTtsClient`
- `VoiceSession`
- `DaemonWorker`

ライフタイムも深く考えすぎず、基本は `Singleton` か `Transient` のどちらかで十分。

---

# 実行モデル

## daemon

### 流れ

1. 起動
2. wake word 待機
3. 検出
4. `VoiceSession.RunFromMicAsync()`
5. 待機に戻る

## oneshot record

- 録音だけする

## oneshot transcribe

- wav を STT に通す

## oneshot ask

- テキストを OpenClaw に送る

## oneshot roundtrip

- 録音 → STT → OpenClaw → TTS → 再生

## oneshot wake-test

- wake word 検出だけ確認する

## oneshot speak

- テキストを TTS に流して再生する

---

# `VoiceSession` の形

```csharp
public sealed class VoiceSession
{
    private readonly AudioIO _audio;
    private readonly WhisperStt _stt;
    private readonly OpenClawClient _openClaw;
    private readonly ITtsClient _tts;

    public async Task RunFromMicAsync(CancellationToken ct)
    {
        var inputWav = await _audio.RecordUntilSilenceAsync(ct);
        var text = await _stt.TranscribeAsync(inputWav, ct);

        if (string.IsNullOrWhiteSpace(text))
            return;

        var responseText = await _openClaw.AskAsync(text, ct);

        if (string.IsNullOrWhiteSpace(responseText))
            return;

        var responseWav = await _tts.SynthesizeAsync(responseText, ct);

        if (!string.IsNullOrWhiteSpace(responseWav))
            await _audio.PlayAsync(responseWav, ct);
    }
}
```

このくらい単純でよい。

---

# Generic Host の使い方

今回 Generic Host を使う理由は、 **設計を立派にするためではなく、daemon を普通に動かすため**。

必要なものだけ使う。

使うもの:

- Configuration
- Logging
- DI
- HostedService
- systemd lifetime 統合

使わないもの:

- 過剰なレイヤー分離
- 複雑な composition root 分割
- 小さすぎる service 群への分割

## 実装方針

- `DaemonWorker` は `BackgroundService` 継承で実装
- Host には `Microsoft.Extensions.Hosting.Systemd` を組み込む
- systemd 配下では notify/logging が有効になる
- 通常のターミナル実行ではそのまま CLI アプリとして動く

# ConsoleAppFramework の使い方

ConsoleAppFramework も、薄く使う。

やること:

- サブコマンド定義
- 引数受け取り
- サービス呼び出し

やらないこと:

- ビジネスロジック本体
- 状態管理
- 外部プロセス制御

---

# 今やらないこと

- `Application / Infrastructure / Contracts` の3層分離
- interface の大量導入
- 状態機械クラス
- CommandHandler クラス乱立
- Options クラスの細分化
- ProcessRunner などの抽象化
- TempFileManager の抽象化
- 将来の node 化を見越した過剰な境界作成

---

# いつ複雑化してよいか

以下のどれかが起きたら、その時点で分割を考える。

- TTS 実装を2種類以上切り替えたくなった
- Audio 実装が ALSA 以外にも増えた
- wake word 実装を複数持ちたくなった
- テストが書きづらくなった
- `VoiceSession` が 300 行を超えて読みにくくなった
- `AppOptions` が肥大化して見通しが悪くなった

つまり、**問題が起きてから分ける**。

---

# systemd 統合方針

`Microsoft.Extensions.Hosting.Systemd` を使う。

## 目的

- systemd 配下での起動/停止を Generic Host に自然に統合する
- systemd の readiness notification を使えるようにする
- console logging を systemd 形式に寄せる

## 方針

- daemon モードは `BackgroundService` ベースで実装する
- Host 構築時に systemd 統合を有効にする
- systemd unit は `Type=notify` を前提にする
- ローカル手動実行時にも動くよう、systemd 統合の有効化は context-aware な挙動に乗る

## 実装メモ

- `Microsoft.Extensions.Hosting.Systemd` パッケージを参照する
- Host 構築時に `AddSystemd()` もしくは `UseSystemd()` の系統を使う
- この機能は systemd サービスとして動いているときだけ有効化される

## この構成での扱い

- `daemon` は systemd サービスとして起動
- `oneshot` は通常の CLI 実行として起動
- 同じバイナリを使い、起動形態だけ分ける

# 採用するシンプル構成

- Generic Host を使う
- ConsoleAppFramework を使う
- Worker は `DaemonWorker` 1つだけ
- 会話本体は `VoiceSession` 1つに集約
- 音声 I/O は `AudioIO` 1つ
- STT は `WhisperStt` 1つ
- OpenClaw 通信は `OpenClawClient` 1つ
- TTS だけ `ITtsClient` で逃がす
- 設定は `AppOptions` 1つ

