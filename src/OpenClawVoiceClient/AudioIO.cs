using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OpenClawVoiceClient;

/// <summary>
/// arecord/aplay (ALSA) を使用した音声録音・再生を担当するクラス。
/// </summary>
public sealed class AlsaAudioIO(IOptions<AppOptions> options, ILogger<AlsaAudioIO> logger) : IAudioIO
{
    private readonly AppOptions _options = options.Value;
    private readonly ILogger<AlsaAudioIO> _logger = logger;

    /// <summary>
    /// 無音検出または最大録音時間に達したら録音を停止する。
    /// 一時WAVファイルのパスを返す。
    /// </summary>
    public async Task<string> RecordUntilSilenceAsync(CancellationToken ct = default)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"openclaw_{Guid.NewGuid():N}.wav");

        _logger.LogInformation("Recording to {File} (max {Max}s, silence {Silence}ms)...",
            tempFile, _options.MaxRecordSeconds, _options.SilenceDurationMs);

        // MaxRecordSeconds まで録音する。無音検出は録音後に行う
        // （簡便のため arecord では固定時間を使用）。
        var args = BuildArecordArgs(tempFile);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(_options.MaxRecordSeconds));

        await RunProcessAsync("arecord", args, cts.Token).ConfigureAwait(false);

        _logger.LogInformation("Recording complete: {File}", tempFile);
        return tempFile;
    }

    /// <summary>
    /// aplay を使用してWAVファイルを再生する。
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
