namespace OpenClawVoiceClient;

/// <summary>
/// 応答テキストを標準出力に書き出す TTS 実装。
/// デバッグ用：音声合成は行わない。
/// </summary>
public sealed class ConsoleTtsClient : ITtsClient
{
    public Task<string?> SynthesizeAsync(string text, CancellationToken ct = default)
    {
        Console.WriteLine(text);
        return Task.FromResult<string?>(null);
    }
}
