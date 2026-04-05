#if WINDOWS
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace OpenClawVoiceClient;

/// <summary>
/// NAudio を使用して WASAPI（共有モード）で Windows の音声録音・再生を担当するクラス。
/// 録音された音声は Whisper との互換性のため 16 kHz / 16-bit / モノラルにリサンプルされる。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsAudioIO(IOptions<AppOptions> options, ILogger<WindowsAudioIO> logger) : IAudioIO
{
    private readonly AppOptions _options = options.Value;
    private readonly ILogger<WindowsAudioIO> _logger = logger;

    /// <inheritdoc />
    public async Task<string> RecordUntilSilenceAsync(CancellationToken ct = default)
    {
        // 一時ファイルをで2つ使用：生キャプチャ（デバイスネイティブ形式）と最終リサンプル済みファイル。
        var tempRaw = Path.Combine(Path.GetTempPath(), $"openclaw_raw_{Guid.NewGuid():N}.wav");
        var tempFinal = Path.Combine(Path.GetTempPath(), $"openclaw_{Guid.NewGuid():N}.wav");

        _logger.LogInformation("Recording (WASAPI) to {File} (max {Max}s)...", tempFinal, _options.MaxRecordSeconds);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(_options.MaxRecordSeconds));

        // デバイスネイティブ形式でキャプチャする（共有モード、デフォルトデバイス）。
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
                // DataAvailable イベントがすべてフラッシュされるよう RecordingStopped を待機する。
                await recordingComplete.Task.ConfigureAwait(false);
            }
        }

        // 生キャプチャを 16 kHz / 16-bit / モノラルにリサンプルする（Whisper の要件）。
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
#endif
