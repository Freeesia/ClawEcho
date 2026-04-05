using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OpenClawVoiceClient;

/// <summary>
/// Waits for a wake word using an external wake word detection tool (e.g. openWakeWord via a subprocess).
/// This is a simple implementation that invokes a Python-based wake word model.
/// Replace or extend as needed.
/// </summary>
public sealed class WakeWordDetector(IOptions<AppOptions> options, ILogger<WakeWordDetector> logger)
{
    private readonly AppOptions _options = options.Value;
    private readonly ILogger<WakeWordDetector> _logger = logger;

    /// <summary>
    /// Waits until a wake word is detected, then returns.
    /// Throws OperationCanceledException if ct is cancelled.
    /// </summary>
    public async Task WaitForWakeWordAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Waiting for wake word (model: {Model}, threshold: {Threshold})...",
            _options.WakeWordModelPath, _options.WakeWordThreshold);

        // Use a Python helper script or dedicated binary for wake word detection.
        // The subprocess is expected to print "WAKE" to stdout when the wake word is detected.
        var startInfo = new ProcessStartInfo
        {
            FileName = "python3",
            Arguments = $"-m openwakeword --model {_options.WakeWordModelPath} --threshold {_options.WakeWordThreshold:F2} --device {_options.InputDevice}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = new Process { StartInfo = startInfo };

        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                _logger.LogDebug("[wakeword] {Line}", e.Data);
        };

        process.Start();
        process.BeginErrorReadLine();

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await process.StandardOutput.ReadLineAsync(ct).ConfigureAwait(false);
                if (line == null)
                {
                    // Process ended unexpectedly
                    _logger.LogWarning("Wake word detector process exited unexpectedly.");
                    await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
                    return;
                }

                if (line.Contains("WAKE", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("Wake word detected.");
                    return;
                }
            }
        }
        finally
        {
            if (!process.HasExited)
                process.Kill();
        }
    }
}
