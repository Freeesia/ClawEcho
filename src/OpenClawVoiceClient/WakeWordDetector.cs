using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OpenClawVoiceClient;

/// <summary>
/// 外部ウェイクワード検出ツール（例：サブプロセス経由の openWakeWord）を使用してウェイクワードを待機する。
/// Pythonベースのウェイクワードモデルを呼び出すシンプルな実装。
/// 必要に応じて差し替えや拡張が可能。
/// </summary>
public sealed class WakeWordDetector(IOptions<AppOptions> options, ILogger<WakeWordDetector> logger)
{
    private readonly AppOptions _options = options.Value;
    private readonly ILogger<WakeWordDetector> _logger = logger;

    /// <summary>
    /// ウェイクワードが検出されるまで待機して戻る。
    /// ct がキャンセルされた場合は OperationCanceledException をスローする。
    /// </summary>
    public async Task WaitForWakeWordAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Waiting for wake word (model: {Model}, threshold: {Threshold})...",
            _options.WakeWordModelPath, _options.WakeWordThreshold);

        // ウェイクワード検出にはPythonヘルパースクリプトまたは専用バイナリを使用する。
        // ウェイクワード検出時にサブプロセスが標準出力に "WAKE" を出力することを期待する。
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
                    // プロセスが予期せず終了した
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
