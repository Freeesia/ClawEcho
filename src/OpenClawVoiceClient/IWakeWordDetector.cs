namespace OpenClawVoiceClient;

public interface IWakeWordDetector
{
    /// <summary>
    /// ウェイクワードが検出されるまで待機して戻る。
    /// ct がキャンセルされた場合は OperationCanceledException をスローする。
    /// </summary>
    Task WaitForWakeWordAsync(CancellationToken ct = default);
}
