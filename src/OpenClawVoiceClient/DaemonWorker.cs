using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace OpenClawVoiceClient;

/// <summary>
/// Background service for daemon mode.
/// Waits for a wake word, then runs a VoiceSession, then repeats.
/// </summary>
public sealed class DaemonWorker : BackgroundService
{
    private readonly WakeWordDetector _wakeWord;
    private readonly VoiceSession _session;
    private readonly ILogger<DaemonWorker> _logger;

    public DaemonWorker(
        WakeWordDetector wakeWord,
        VoiceSession session,
        ILogger<DaemonWorker> logger)
    {
        _wakeWord = wakeWord;
        _session = session;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Daemon started. Listening for wake word...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("[State] WaitingForWake");
                await _wakeWord.WaitForWakeWordAsync(stoppingToken).ConfigureAwait(false);

                _logger.LogInformation("[State] WakeDetected - wake word triggered.");
                await _session.RunFromMicAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Shutdown requested
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during voice session. Restarting loop...");
                // Brief pause before retry to avoid tight error loops
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false);
            }
        }

        _logger.LogInformation("Daemon stopped.");
    }
}
