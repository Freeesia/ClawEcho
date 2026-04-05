using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace OpenClawVoiceClient;

/// <summary>
/// Handles audio recording and playback on Windows using WASAPI (shared mode) via NAudio.
/// Recorded audio is resampled to 16 kHz / 16-bit / mono for Whisper compatibility.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsAudioIO(IOptions<AppOptions> options, ILogger<WindowsAudioIO> logger) : IAudioIO
{
    private readonly AppOptions _options = options.Value;
    private readonly ILogger<WindowsAudioIO> _logger = logger;

    /// <inheritdoc />
    public async Task<string> RecordUntilSilenceAsync(CancellationToken ct = default)
    {
        // Two temp files: raw capture (device native format) and final resampled file.
        var tempRaw = Path.Combine(Path.GetTempPath(), $"openclaw_raw_{Guid.NewGuid():N}.wav");
        var tempFinal = Path.Combine(Path.GetTempPath(), $"openclaw_{Guid.NewGuid():N}.wav");

        _logger.LogInformation("Recording (WASAPI) to {File} (max {Max}s)...", tempFinal, _options.MaxRecordSeconds);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(_options.MaxRecordSeconds));

        // Capture in device-native format (shared mode, default device).
        using var capture = new WasapiCapture();
        using (var writer = new WaveFileWriter(tempRaw, capture.WaveFormat))
        {
            var recordingComplete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            capture.DataAvailable += (_, e) =>
            {
                if (e.BytesRecorded > 0)
                    writer.Write(e.Buffer, 0, e.BytesRecorded);
            };

            capture.RecordingStopped += (_, _) => recordingComplete.TrySetResult();

            capture.StartRecording();

            try
            {
                await Task.Delay(Timeout.Infinite, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            finally
            {
                capture.StopRecording();
                // Wait for RecordingStopped so all DataAvailable events have flushed.
                await recordingComplete.Task.ConfigureAwait(false);
            }
        }

        // Resample raw capture → 16 kHz / 16-bit / mono (required by Whisper).
        var targetFormat = new WaveFormat(_options.SampleRate, 16, _options.Channels);
        using (var reader = new WaveFileReader(tempRaw))
        using (var resampler = new MediaFoundationResampler(reader, targetFormat) { ResamplerQuality = 60 })
        {
            WaveFileWriter.CreateWaveFile(tempFinal, resampler);
        }

        try { File.Delete(tempRaw); } catch { }

        _logger.LogInformation("Recording complete: {File}", tempFinal);
        return tempFinal;
    }

    /// <inheritdoc />
    public async Task PlayAsync(string wavFile, CancellationToken ct = default)
    {
        _logger.LogInformation("Playing {File}...", wavFile);

        var playbackComplete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var reader = new AudioFileReader(wavFile);
        using var player = new WasapiOut();

        player.PlaybackStopped += (_, _) => playbackComplete.TrySetResult();
        player.Init(reader);
        player.Play();

        using var _ = ct.Register(() => player.Stop());
        await playbackComplete.Task.ConfigureAwait(false);

        _logger.LogInformation("Playback complete.");
    }
}
