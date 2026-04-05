using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Whisper.net;

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

            if (string.IsNullOrWhiteSpace(_options.WhisperModelPath))
                throw new InvalidOperationException("WhisperModelPath is not configured.");

            _logger.LogInformation("Loading Whisper model from {Path}...", _options.WhisperModelPath);
            _factory = WhisperFactory.FromPath(_options.WhisperModelPath);
            return _factory;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public void Dispose()
    {
        _factory?.Dispose();
        _initLock.Dispose();
    }
}
