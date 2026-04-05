using ConsoleAppFramework;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace OpenClawVoiceClient;

/// <summary>
/// CLI commands for OpenClawVoiceClient.
/// Provides daemon (continuous) and oneshot (single-task) modes.
/// </summary>
public sealed class Commands(
    DaemonWorker daemon,
    VoiceSession session,
    IAudioIO audio,
    WhisperStt stt,
    OpenClawClient openClaw,
    WakeWordDetector wakeWord,
    ITtsClient tts,
    ILogger<Commands> logger)
{
    /// <summary>
    /// Starts the daemon: waits for wake word, runs a voice session, repeats until stopped.
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
            // Normal shutdown via Ctrl+C or SIGTERM
        }
        finally
        {
            await ((IHostedService)daemon).StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Records from the microphone until silence or max duration, saves to a temp WAV file.
    /// </summary>
    [Command("oneshot record")]
    public async Task OneshotRecordAsync(CancellationToken cancellationToken)
    {
        var file = await audio.RecordUntilSilenceAsync(cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Recorded to: {File}", file);
    }

    /// <summary>
    /// Transcribes a WAV file to text using Whisper.
    /// </summary>
    /// <param name="wavFile">Path to the WAV file to transcribe.</param>
    [Command("oneshot transcribe")]
    public async Task OneshotTranscribeAsync([Argument] string wavFile, CancellationToken cancellationToken)
    {
        var text = await stt.TranscribeAsync(wavFile, cancellationToken).ConfigureAwait(false);
        Console.WriteLine(text);
    }

    /// <summary>
    /// Sends text to OpenClaw and prints the response.
    /// </summary>
    /// <param name="text">The text to send to OpenClaw.</param>
    [Command("oneshot ask")]
    public async Task OneshotAskAsync([Argument] string text, CancellationToken cancellationToken)
    {
        var response = await openClaw.AskAsync(text, cancellationToken).ConfigureAwait(false);
        Console.WriteLine(response);
    }

    /// <summary>
    /// Full round-trip: records from mic → STT → OpenClaw → TTS → plays response.
    /// </summary>
    [Command("oneshot roundtrip")]
    public async Task OneshotRoundtripAsync(CancellationToken cancellationToken)
    {
        await session.RunFromMicAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Tests wake word detection: waits for a wake word and prints a confirmation.
    /// </summary>
    [Command("oneshot wake-test")]
    public async Task OneshotWakeTestAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Waiting for wake word...");
        await wakeWord.WaitForWakeWordAsync(cancellationToken).ConfigureAwait(false);
        Console.WriteLine("Wake word detected!");
    }

    /// <summary>
    /// Synthesizes text to speech and plays it back.
    /// </summary>
    /// <param name="text">The text to speak.</param>
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
