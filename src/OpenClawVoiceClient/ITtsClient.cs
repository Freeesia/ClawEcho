namespace OpenClawVoiceClient;

public interface ITtsClient
{
    /// <summary>
    /// 指定されたテキストをWAVファイルに音声合成してパスを返す。
    /// 音声合成が利用できない場合は null または空文字を返す。
    /// </summary>
    Task<string?> SynthesizeAsync(string text, CancellationToken ct = default);
}
