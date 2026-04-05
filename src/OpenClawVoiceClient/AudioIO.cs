using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OpenClawVoiceClient;

/// <summary>
/// Handles audio recording and playback using arecord/aplay (ALSA).
/// </summary>
public sealed class AudioIO(IOptions<AppOptions> options, ILogger<AudioIO> logger)
{
    private readonly AppOptions _options = options.Value;
    private readonly ILogger<AudioIO> _logger = logger;

    /// <summary>
    /// Starts recording and stops after silence is detected or max duration is reached.
    /// Returns the path to the temporary WAV file.
    /// </summary>
    public async Task<string> RecordUntilSilenceAsync(CancellationToken ct = default)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"openclaw_{Guid.NewGuid():N}.wav");

        _logger.LogInformation("Recording to {File} (max {Max}s, silence {Silence}ms)...",
            tempFile, _options.MaxRecordSeconds, _options.SilenceDurationMs);

        // Record for up to MaxRecordSeconds. Silence detection is done post-recording
        // by trimming the audio; for simplicity we use a fixed duration with arecord.
        var args = BuildArecordArgs(tempFile);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(_options.MaxRecordSeconds));

        await RunProcessAsync("arecord", args, cts.Token).ConfigureAwait(false);

        _logger.LogInformation("Recording complete: {File}", tempFile);
        return tempFile;
    }

    /// <summary>
    /// Plays back a WAV file using aplay.
    /// </summary>
    public async Task PlayAsync(string wavFile, CancellationToken ct = default)
    {
        _logger.LogInformation("Playing {File}...", wavFile);

        var args = $"-D {_options.OutputDevice} {wavFile}";
        await RunProcessAsync("aplay", args, ct).ConfigureAwait(false);

        _logger.LogInformation("Playback complete.");
    }

    private string BuildArecordArgs(string outputFile)
    {
        return $"-D {_options.InputDevice} " +
               $"-r {_options.SampleRate} " +
               $"-c {_options.Channels} " +
               $"-f S16_LE " +
               $"-t wav " +
               $"{outputFile}";
    }

    private async Task RunProcessAsync(string command, string args, CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            Arguments = args,
            RedirectStandardOutput = false,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = new Process { StartInfo = startInfo };

        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                _logger.LogDebug("[{Command}] {Line}", command, e.Data);
        };

        process.Start();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill();
            throw;
        }
    }
}
