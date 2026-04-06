using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NanoWakeWord;
#if WINDOWS
using System.Runtime.Versioning;
using NAudio.Wave;
#endif

namespace OpenClawVoiceClient;

/// <summary>
/// NanoWakeWord を使用してクロスプラットフォームでウェイクワードを検出するクラス。
/// Windows: NAudio（WaveInEvent）で 16 kHz モノラル PCM をキャプチャ。
/// Linux: arecord サブプロセスで RAW PCM をキャプチャしてNanoWakeWordに渡す。
/// </summary>
#if WINDOWS
[SupportedOSPlatform("windows")]
#endif
public sealed class WakeWordDetector(IOptions<AppOptions> options, ILogger<WakeWordDetector> logger) : IWakeWordDetector
{
    private readonly AppOptions _options = options.Value;
    private readonly ILogger<WakeWordDetector> _logger = logger;

    // NanoWakeWord が期待する形式: 16 kHz / 16-bit / モノラル PCM
    private const int SampleRate = 16000;
    private const int BitsPerSample = 16;
    private const int Channels = 1;

    // NanoWakeWord の内部チャンクサイズ (1280 サンプル × 2 バイト = 2560 バイト)
    private const int ChunkSamples = 1280;
    private const int ChunkBytes = ChunkSamples * 2;

    // WaveInEvent バッファサイズ（1280 サンプル @ 16 kHz ≒ 80 ms）
    private const int BufferMilliseconds = 80;

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
#if WINDOWS
            await WaitWithWaveInAsync(runtime, ct).ConfigureAwait(false);
#else
            await WaitWithArecordAsync(runtime, ct).ConfigureAwait(false);
#endif
        }
    }

#if WINDOWS
    private async Task WaitWithWaveInAsync(WakeWordRuntime runtime, CancellationToken ct)
    {
        var detectionTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // WaveInEvent は 16 kHz / 16-bit / モノラルを直接要求できるため、
        // ドライバがリサンプリングを担当する。複数アプリからの同時使用も可能。
        using var waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(SampleRate, BitsPerSample, Channels),
            BufferMilliseconds = BufferMilliseconds
        };

        waveIn.DataAvailable += (_, e) =>
        {
            if (e.BytesRecorded <= 0 || detectionTcs.Task.IsCompleted)
                return;

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
#else
    private async Task WaitWithArecordAsync(WakeWordRuntime runtime, CancellationToken ct)
    {
        // arecord で 16 kHz / 16-bit / モノラルの RAW PCM を標準出力に出力させ、
        // NanoWakeWord のチャンクサイズ（1280 サンプル）単位で読み込む。
        var startInfo = new ProcessStartInfo
        {
            FileName = "arecord",
            Arguments = $"-D {_options.InputDevice} -f S16_LE -r {SampleRate} -c {Channels} -t raw",
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

        var buffer = new byte[ChunkBytes];
        var samples = new short[ChunkSamples];

        try
        {
            using var stream = process.StandardOutput.BaseStream;
            while (!ct.IsCancellationRequested)
            {
                // ストリームから正確に ChunkBytes バイト読み込む
                int totalRead = 0;
                while (totalRead < ChunkBytes)
                {
                    int bytesRead = await stream.ReadAsync(
                        buffer.AsMemory(totalRead, ChunkBytes - totalRead), ct).ConfigureAwait(false);
                    if (bytesRead == 0)
                    {
                        _logger.LogWarning("arecord process ended unexpectedly.");
                        return;
                    }
                    totalRead += bytesRead;
                }

                Buffer.BlockCopy(buffer, 0, samples, 0, ChunkBytes);
                if (runtime.Process(samples) >= 0)
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
#endif

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

