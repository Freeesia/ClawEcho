using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Whisper.net;
using Whisper.net.Ggml;

namespace OpenClawVoiceClient;

/// <summary>
/// Speech-to-text using Whisper.net.
/// </summary>
public sealed class WhisperStt(IOptions<AppOptions> options, ILogger<WhisperStt> logger) : IDisposable
{
    private readonly AppOptions _options = options.Value;
    private readonly ILogger<WhisperStt> _logger = logger;
    private WhisperFactory? _factory;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    /// <summary>
    /// Transcribes the given WAV file and returns the recognized text.
    /// </summary>
    public async Task<string> TranscribeAsync(string wavFile, CancellationToken ct = default)
    {
        var factory = await GetOrCreateFactoryAsync(ct).ConfigureAwait(false);

        _logger.LogInformation("Transcribing {File}...", wavFile);

        using var processor = factory.CreateBuilder()
            .WithLanguage(_options.WhisperLanguage)
            .Build();

        var segments = new List<string>();

        await using var stream = File.OpenRead(wavFile);
        await foreach (var segment in processor.ProcessAsync(stream, ct).ConfigureAwait(false))
        {
            segments.Add(segment.Text.Trim());
        }

        var result = string.Join(" ", segments).Trim();
        _logger.LogInformation("Transcription result: {Text}", result);
        return result;
    }

    private async Task<WhisperFactory> GetOrCreateFactoryAsync(CancellationToken ct)
    {
        if (_factory != null)
            return _factory;

        await _initLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_factory != null)
                return _factory;

            var modelPath = ResolveModelPath();
            await EnsureModelExistsAsync(modelPath, ct).ConfigureAwait(false);

            _logger.LogInformation("Loading Whisper model from {Path}...", modelPath);
            _factory = WhisperFactory.FromPath(modelPath);
            return _factory;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private string ResolveModelPath()
    {
        if (!string.IsNullOrWhiteSpace(_options.WhisperModelPath))
            return _options.WhisperModelPath;

        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClawEcho");
        return Path.Combine(dir, $"ggml-{_options.WhisperModelType.ToString().ToLowerInvariant()}.bin");
    }

    private async Task EnsureModelExistsAsync(string modelPath, CancellationToken ct)
    {
        if (File.Exists(modelPath))
            return;

        var ggmlType = _options.WhisperModelType;
        _logger.LogInformation("Whisper model not found at {Path}. Downloading {Type}...", modelPath, ggmlType);

        var dir = Path.GetDirectoryName(modelPath)!;
        Directory.CreateDirectory(dir);

        var tmpPath = modelPath + ".tmp";
        try
        {
            await using var modelStream = await WhisperGgmlDownloader
                .GetGgmlModelAsync(ggmlType, QuantizationType.NoQuantization, ct).ConfigureAwait(false);
            await using var fileStream = File.OpenWrite(tmpPath);
            await modelStream.CopyToAsync(fileStream, ct).ConfigureAwait(false);
        }
        catch
        {
            try { File.Delete(tmpPath); } catch { }
            throw;
        }

        File.Move(tmpPath, modelPath);
        _logger.LogInformation("Whisper model downloaded to {Path}.", modelPath);
    }

    public void Dispose()
    {
        _factory?.Dispose();
        _initLock.Dispose();
    }
}
