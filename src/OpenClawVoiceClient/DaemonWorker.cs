using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace OpenClawVoiceClient;

/// <summary>
/// デーモンモード用バックグラウンドサービス。
/// ウェイクワードを待機し、VoiceSession を実行するループを繰り返す。
/// </summary>
public sealed class DaemonWorker(
    WakeWordDetector wakeWord,
    VoiceSession session,
    ILogger<DaemonWorker> logger) : BackgroundService
{
    private readonly WakeWordDetector _wakeWord = wakeWord;
    private readonly VoiceSession _session = session;
    private readonly ILogger<DaemonWorker> _logger = logger;

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
                // シャットダウン要求
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during voice session. Restarting loop...");
                // タイトなエラーループを避けるため、リトライ前に短時間待機
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false);
            }
        }

        _logger.LogInformation("Daemon stopped.");
    }
}
