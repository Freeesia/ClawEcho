#if WINDOWS
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NAudio.Wave;
using NanoWakeWord;

namespace OpenClawVoiceClient;

/// <summary>
/// NAudio（WaveInEvent）と NanoWakeWord を使用して Windows でウェイクワードを検出するクラス。
/// WaveInEvent は 16 kHz / 16-bit / モノラルを直接要求でき、ドライバが共有アクセスとリサンプリングを担当する。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsWakeWordDetector(IOptions<AppOptions> options, ILogger<WindowsWakeWordDetector> logger)
    : IWakeWordDetector
{
    private readonly AppOptions _options = options.Value;
    private readonly ILogger<WindowsWakeWordDetector> _logger = logger;

    // NanoWakeWord が期待する形式: 16 kHz / 16-bit / モノラル PCM
    private const int SampleRate = 16000;
    private const int BitsPerSample = 16;
    private const int Channels = 1;

    // NanoWakeWord の内部チャンクサイズ（1280 サンプル）の整数倍に合わせたバッファサイズ
    private const int BufferMilliseconds = 80; // 1280 samples at 16 kHz

    // NanoWakeWord の初期化（作業ディレクトリ変更 + ファイルコピー）をプロセス内でシリアライズするためのロック
    private static readonly Lock RuntimeInitLock = new();

    /// <inheritdoc />
    public async Task WaitForWakeWordAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Waiting for wake word (model: {Model}, threshold: {Threshold})...",
            _options.WakeWordModelPath, _options.WakeWordThreshold);

        var modelName = PrepareModel(_options.WakeWordModelPath);

        // NanoWakeWord はモデルファイルを相対パス "models/<name>.onnx" で参照するため、
        // WakeWordRuntime の生成中だけ作業ディレクトリを AppContext.BaseDirectory に変更する。
        // Directory.SetCurrentDirectory はプロセス共有のため静的ロックで保護する。
        WakeWordRuntime runtime;
        lock (RuntimeInitLock)
        {
            var originalDir = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(AppContext.BaseDirectory);
            try
            {
                runtime = new WakeWordRuntime(new WakeWordRuntimeConfig
                {
                    WakeWords = [new WakeWordConfig { Model = modelName, Threshold = _options.WakeWordThreshold }],
                    DebugAction = (model, probability, isDetected) =>
                    {
                        if (isDetected)
                            _logger.LogDebug("[wakeword] {Model}: {Probability:F3} (DETECTED)", model, probability);
                    }
                });
            }
            finally
            {
                Directory.SetCurrentDirectory(originalDir);
            }
        }

        using (runtime)
        {
            var detectionTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            using var waveIn = new WaveInEvent
            {
                WaveFormat = new WaveFormat(SampleRate, BitsPerSample, Channels),
                BufferMilliseconds = BufferMilliseconds
            };

            waveIn.DataAvailable += (_, e) =>
            {
                if (e.BytesRecorded <= 0 || detectionTcs.Task.IsCompleted)
                    return;

                // byte[] → short[] に変換して NanoWakeWord に渡す
                var samples = new short[e.BytesRecorded / 2];
                Buffer.BlockCopy(e.Buffer, 0, samples, 0, e.BytesRecorded);

                if (runtime.Process(samples) >= 0)
                {
                    _logger.LogInformation("Wake word detected.");
                    detectionTcs.TrySetResult();
                }
            };

            waveIn.StartRecording();
            try
            {
                using var cancelReg = ct.Register(() => detectionTcs.TrySetCanceled(ct));
                await detectionTcs.Task.ConfigureAwait(false);
            }
            finally
            {
                waveIn.StopRecording();
            }
        }
    }

    /// <summary>
    /// AppOptions.WakeWordModelPath を NanoWakeWord が受け付けるモデル名（拡張子なし）に変換する。
    /// フルパスが指定された場合は AppContext.BaseDirectory/models/ にファイルをコピーする。
    /// </summary>
    private static string PrepareModel(string modelPath)
    {
        // パス区切り文字を含まない場合はビルトインモデル名または単純な名前として扱う
        if (Path.GetFileName(modelPath) == modelPath)
            return Path.GetFileNameWithoutExtension(modelPath);

        var modelName = Path.GetFileNameWithoutExtension(modelPath);
        var targetDir = Path.Combine(AppContext.BaseDirectory, "models");
        var targetPath = Path.Combine(targetDir, modelName + ".onnx");

        if (!File.Exists(targetPath))
        {
            lock (RuntimeInitLock)
            {
                if (!File.Exists(targetPath))
                {
                    if (!File.Exists(modelPath))
                        throw new FileNotFoundException($"ウェイクワードモデルファイルが見つかりません: {modelPath}");

                    Directory.CreateDirectory(targetDir);
                    File.Copy(modelPath, targetPath);
                }
            }
        }

        return modelName;
    }
}
#endif
