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
/// RMS ベースの無音検出により、ユーザーが発話を終了したタイミングで録音を自動停止する。
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

            // RMS 無音検出用の状態変数
            // DataAvailable イベントは NAudio の内部キャプチャスレッドから直列に呼び出されるため、
            // これらの変数へのアクセスは DataAvailable コールバック内のみに限定する。
            bool hasSpeechStarted = false;
            var silenceStopwatch = new System.Diagnostics.Stopwatch();
            var silenceThreshold = (float)_options.SilenceThreshold;
            var silenceDurationMs = _options.SilenceDurationMs;

            capture.DataAvailable += (_, e) =>
            {
                if (e.BytesRecorded <= 0) return;

                writer.Write(e.Buffer, 0, e.BytesRecorded);

                // RMS を計算して発話終了を検出する
                var rms = CalculateRms(e.Buffer, e.BytesRecorded, capture.WaveFormat);
                if (rms >= silenceThreshold)
                {
                    // 音声検出：無音タイマーをリセット
                    hasSpeechStarted = true;
                    silenceStopwatch.Reset();
                }
                else if (hasSpeechStarted)
                {
                    // 発話開始後の無音区間を計測する
                    if (!silenceStopwatch.IsRunning)
                        silenceStopwatch.Start();

                    if (silenceStopwatch.ElapsedMilliseconds >= silenceDurationMs)
                        cts.Cancel(); // 無音が十分続いたので録音を終了する
                }
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

    /// <summary>
    /// PCM バッファの RMS（二乗平均平方根）振幅を計算する。
    /// 無音検出のしきい値との比較に使用する。
    /// </summary>
    private static float CalculateRms(byte[] buffer, int bytesRecorded, WaveFormat format)
    {
        double sum = 0;
        int count = 0;

        if (format.BitsPerSample == 32)
        {
            // 32-bit float（WASAPI 共有モードの一般的な形式）
            int floatCount = bytesRecorded / 4;
            for (int i = 0; i < floatCount; i++)
            {
                float sample = BitConverter.ToSingle(buffer, i * 4);
                sum += sample * sample;
                count++;
            }
        }
        else if (format.BitsPerSample == 16)
        {
            // 16-bit PCM
            int sampleCount = bytesRecorded / 2;
            for (int i = 0; i < sampleCount; i++)
            {
                float sample = BitConverter.ToInt16(buffer, i * 2) / 32768f;
                sum += sample * sample;
                count++;
            }
        }

        return count > 0 ? (float)Math.Sqrt(sum / count) : 0f;
    }
}
#endif

