# TODO

## 現在の実装状態

- [x] ConsoleAppFramework による CLI
  - [x] `daemon`
  - [x] `oneshot record`
  - [x] `oneshot transcribe`
  - [x] `oneshot ask`
  - [x] `oneshot roundtrip`
  - [x] `oneshot wake-test`
  - [x] `oneshot speak`
- [x] dotnet tool としてパッケージ化可能にする
  - [x] `ToolCommandName` を `clawecho` に設定
  - [x] `install` / `uninstall` コマンドで systemd サービスを登録・解除
- [x] 設定読み込み
  - [x] 同梱 `appsettings.json`
  - [x] ユーザー設定ディレクトリの `appsettings.json`
  - [x] ユーザー設定ディレクトリの `appsettings.Local.json`
  - [x] `OPENCLAW_` 環境変数
- [x] 音声 I/O
  - [x] `IAudioIO` で Linux / Windows 実装を切り替え
  - [x] Linux: `arecord` / `aplay`
  - [x] Windows: NAudio + WASAPI 共有モード
  - [x] Windows 録音を Whisper 向けに 16 kHz / 16-bit / mono へリサンプル
- [x] Whisper.net による STT
  - [x] ggml モデルの自動ダウンロード
  - [x] モデル初期化の排他制御
- [x] OpenClaw 連携
  - [x] OpenAI SDK の Responses API で `/v1/responses` に送信
  - [x] `PreviousResponseId` によるセッション継続
  - [x] `SessionUser` の送信
- [x] TTS の差し替え口
  - [x] `ITtsClient`
  - [x] `ConsoleTtsClient` によるデバッグ出力
- [x] VS Code / Visual Studio から使いやすい `launchSettings.json`

## 未対応・次にやること

- [ ] GitHub Actions
  - [ ] Pull request で `dotnet build src/ClawEcho.sln`
  - [ ] `main` push 時のリリースまたはデプロイ方針を決める
- [ ] TTS の実装
  - [ ] `TtsEndpoint` の扱いを決める
  - [ ] 音声ファイルを生成する実装を追加する
  - [ ] `oneshot speak` / `roundtrip` で実際に再生できるようにする
- [ ] ユーザーの音声入力終了判定
  - [ ] 現状の録音は `MaxRecordSeconds` による固定長
  - [ ] `SilenceThreshold` / `SilenceDurationMs` を実際の無音検出に使う
- [ ] Wake word 検出の整備
  - [ ] `python3` 固定をやめ、OS に応じて `python3` / `python` / `py` を選択する
  - [ ] `WakeWordModelPath` やデバイス名を安全に引数へ渡す
  - [ ] Windows で `oneshot wake-test` を動作確認する
- [ ] Windows 対応の継続
  - [ ] Windows 用ビルドを GitHub Actions に追加
  - [ ] Windows の録音・再生デバイス選択を設定値に反映する
- [ ] 運用ドキュメント
  - [ ] dotnet tool のインストール手順をリリース方法に合わせて確定する
  - [ ] Raspberry Pi で必要な ALSA / Python / openWakeWord セットアップ手順を追記する
