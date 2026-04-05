# ClawEcho

OpenClaw で会話するスマートスピーカーアプリ（Raspberry Pi 向け半二重音声クライアント）

## 必要条件

- .NET 10 Runtime
- ALSA（`arecord` / `aplay`）
- Whisper ggml モデルファイル
- ウェイクワードモデル（openWakeWord 等）
- OpenClaw サーバー

## 設定

`appsettings.json` を編集するか、環境変数（`OPENCLAW_App__*`）で上書きできます。

```json
{
  "App": {
    "OpenClawBaseUrl": "http://your-openclaw-server:8080",
    "OpenClawBearerToken": "your-token",
    "WhisperModelPath": "/path/to/ggml-base.bin",
    "WakeWordModelPath": "/path/to/wake_word.onnx"
  }
}
```

## 使い方

### 常駐モード（daemon）

```bash
./OpenClawVoiceClient daemon
```

ウェイクワードを待ち、検出したら VoiceSession を実行し、また待機するループを繰り返します。

### 単発モード（oneshot）

```bash
# マイクから録音
./OpenClawVoiceClient oneshot record

# WAV ファイルを文字起こし
./OpenClawVoiceClient oneshot transcribe /path/to/audio.wav

# テキストを OpenClaw に送信
./OpenClawVoiceClient oneshot ask "こんにちは"

# 録音→STT→OpenClaw→TTS→再生のフルラウンドトリップ
./OpenClawVoiceClient oneshot roundtrip

# ウェイクワード検出テスト
./OpenClawVoiceClient oneshot wake-test

# テキストを TTS で再生
./OpenClawVoiceClient oneshot speak "こんにちは"
```

## systemd サービスとして実行

```bash
sudo cp deploy/openclaw-voice-client.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable openclaw-voice-client
sudo systemctl start openclaw-voice-client
```

systemd サービスとして動作しているときは `Type=notify` を使った readiness 通知が有効になります。
