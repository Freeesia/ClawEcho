using OpenAI;
using OpenAI.Responses;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

#pragma warning disable OPENAI001 // ResponsesClient は試験的 API

namespace OpenClawVoiceClient;

/// <summary>
/// OpenClaw の /v1/responses エンドポイント クライアント。
/// </summary>
public sealed class OpenClawClient(IOptions<AppOptions> options, ILogger<OpenClawClient> logger)
{
    private readonly AppOptions _options = options.Value;
    private readonly ILogger<OpenClawClient> _logger = logger;

    // 直前のレスポンスID（会話継続に使用）
    private string? _previousResponseId;

    private ResponsesClient CreateClient()
    {
        var credential = new System.ClientModel.ApiKeyCredential(_options.OpenClawBearerToken);
        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri(_options.OpenClawBaseUrl),
        };
        return new ResponsesClient(credential, clientOptions);
    }

    /// <summary>
    /// ユーザーのテキストをOpenClawに送信し、アシスタントの応答テキストを返す。
    /// </summary>
    public async Task<string> AskAsync(string userText, CancellationToken ct = default)
    {
        _logger.LogInformation("Sending to OpenClaw: {Text}", userText);

        var client = CreateClient();
        var requestOptions = new CreateResponseOptions
        {
            Model = _options.OpenClawModel,
            PreviousResponseId = _previousResponseId,
            EndUserId = string.IsNullOrEmpty(_options.SessionUser) ? null : _options.SessionUser,
        };
        requestOptions.InputItems.Add(ResponseItem.CreateUserMessageItem(userText));

        var response = await client.CreateResponseAsync(requestOptions, ct).ConfigureAwait(false);

        var assistantText = response.Value.GetOutputText() ?? string.Empty;
        _logger.LogInformation("OpenClaw response: {Text}", assistantText);

        _previousResponseId = response.Value.Id;

        return assistantText;
    }

    /// <summary>
    /// 会話履歴をクリアする。
    /// </summary>
    public void ClearHistory() => _previousResponseId = null;
}
