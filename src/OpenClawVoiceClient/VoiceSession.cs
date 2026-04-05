using Microsoft.Extensions.Logging;

namespace OpenClawVoiceClient;

/// <summary>
/// Orchestrates a single voice interaction: record → STT → OpenClaw → TTS → playback.
/// This is the core of the application.
/// </summary>
public sealed class VoiceSession(
    AudioIO audio,
    WhisperStt stt,
    OpenClawClient openClaw,
    ITtsClient tts,
    ILogger<VoiceSession> logger)
{
    private readonly AudioIO _audio = audio;
    private readonly WhisperStt _stt = stt;
    private readonly OpenClawClient _openClaw = openClaw;
    private readonly ITtsClient _tts = tts;
    private readonly ILogger<VoiceSession> _logger = logger;

    /// <summary>
    /// Runs a full voice round-trip: record from mic → transcribe → ask OpenClaw → synthesize → play.
    /// </summary>
    public async Task RunFromMicAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("[State] Recording");
        var inputWav = await _audio.RecordUntilSilenceAsync(ct).ConfigureAwait(false);

        try
        {
            _logger.LogInformation("[State] Transcribing");
            var text = await _stt.TranscribeAsync(inputWav, ct).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(text))
            {
                _logger.LogInformation("No speech detected, skipping.");
                return;
            }

            _logger.LogInformation("[State] CallingOpenClaw");
            var responseText = await _openClaw.AskAsync(text, ct).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(responseText))
            {
                _logger.LogInformation("Empty response from OpenClaw, skipping.");
                return;
            }

            _logger.LogInformation("[State] Speaking");
            var responseWav = await _tts.SynthesizeAsync(responseText, ct).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(responseWav))
            {
                await _audio.PlayAsync(responseWav, ct).ConfigureAwait(false);
            }
            else
            {
                _logger.LogInformation("TTS returned no audio. Response was: {Text}", responseText);
            }
        }
        finally
        {
            TryDeleteTempFile(inputWav);
        }
    }

    private void TryDeleteTempFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete temp file {Path}", path);
        }
    }
}
