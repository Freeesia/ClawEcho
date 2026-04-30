using ConsoleAppFramework;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace OpenClawVoiceClient;

/// <summary>
/// OpenClawVoiceClient の CLIコマンド定義。
/// デーモン（継続）モードとワンショット（単発）モードを提供する。
/// </summary>
public sealed class Commands(
    DaemonWorker daemon,
    VoiceSession session,
    IAudioIO audio,
    WhisperStt stt,
    OpenClawClient openClaw,
    IWakeWordDetector wakeWord,
    ITtsClient tts,
    ILogger<Commands> logger)
{
    /// <summary>
    /// デーモンを開始する。ウェイクワード待機 → 音声セッション実行のループを停止まで繰り返す。
    /// </summary>
    [Command("daemon")]
    public async Task DaemonAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting daemon mode...");

        await ((IHostedService)daemon).StartAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Ctrl+C または SIGTERM による正常シャットダウン
        }
        finally
        {
            await ((IHostedService)daemon).StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 無音または最大録音時間までマイクから録音し、一時WAVファイルに保存する。
    /// </summary>
    [Command("oneshot record")]
    public async Task OneshotRecordAsync(CancellationToken cancellationToken)
    {
        var file = await audio.RecordUntilSilenceAsync(cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Recorded to: {File}", file);
    }

    /// <summary>
    /// Whisper を使用してWAVファイルをテキストに書き起こす。
    /// </summary>
    /// <param name="wavFile">書き起こし対象のWAVファイルのパス。</param>
    [Command("oneshot transcribe")]
    public async Task OneshotTranscribeAsync([Argument] string wavFile, CancellationToken cancellationToken)
    {
        var text = await stt.TranscribeAsync(wavFile, cancellationToken).ConfigureAwait(false);
        Console.WriteLine(text);
    }

    /// <summary>
    /// テキストをOpenClawに送信してレスポンスを表示する。
    /// </summary>
    /// <param name="text">OpenClawに送信するテキスト。</param>
    [Command("oneshot ask")]
    public async Task OneshotAskAsync([Argument] string text, CancellationToken cancellationToken)
    {
        var response = await openClaw.AskAsync(text, cancellationToken).ConfigureAwait(false);
        Console.WriteLine(response);
    }

    /// <summary>
    /// フルラウンドトリップ：マイク録音 → STT → OpenClaw → TTS → 再生。
    /// </summary>
    [Command("oneshot roundtrip")]
    public async Task OneshotRoundtripAsync(CancellationToken cancellationToken)
    {
        await session.RunFromMicAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// ウェイクワード検出のテスト：ウェイクワードを待機して確認メッセージを表示する。
    /// </summary>
    [Command("oneshot wake-test")]
    public async Task OneshotWakeTestAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Waiting for wake word...");
        await wakeWord.WaitForWakeWordAsync(cancellationToken).ConfigureAwait(false);
        Console.WriteLine("Wake word detected!");
    }

    /// <summary>
    /// テキストを音声合成して再生する。
    /// </summary>
    /// <param name="text">読み上げるテキスト。</param>
    [Command("oneshot speak")]
    public async Task OneshotSpeakAsync([Argument] string text, CancellationToken cancellationToken)
    {
        var wavFile = await tts.SynthesizeAsync(text, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(wavFile))
        {
            await audio.PlayAsync(wavFile, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            logger.LogWarning("TTS returned no audio for: {Text}", text);
        }
    }
}