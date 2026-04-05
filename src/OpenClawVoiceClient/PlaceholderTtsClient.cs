using Microsoft.Extensions.Logging;

namespace OpenClawVoiceClient;

/// <summary>
/// プレースホルダー TTS クライアント。合成するテキストをログに記録して null を返す。
/// TTSサービスが利用可能になったら実際の実装に差し替えること。
/// </summary>
public sealed class PlaceholderTtsClient(ILogger<PlaceholderTtsClient> logger) : ITtsClient
{
    private readonly ILogger<PlaceholderTtsClient> _logger = logger;

    public Task<string?> SynthesizeAsync(string text, CancellationToken ct = default)
    {
        _logger.LogInformation("[TTS placeholder] Would synthesize: {Text}", text);
        return Task.FromResult<string?>(null);
    }
}
