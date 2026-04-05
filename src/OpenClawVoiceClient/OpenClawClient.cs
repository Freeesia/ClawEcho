using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OpenClawVoiceClient;

/// <summary>
/// Client for OpenClaw's /v1/responses endpoint.
/// </summary>
public sealed class OpenClawClient
{
    private readonly HttpClient _http;
    private readonly AppOptions _options;
    private readonly ILogger<OpenClawClient> _logger;

    // Simple conversation history stored as a list of messages
    private readonly List<RequestMessage> _history = [];

    public OpenClawClient(HttpClient http, IOptions<AppOptions> options, ILogger<OpenClawClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Sends the user's text to OpenClaw and returns the assistant's response text.
    /// </summary>
    public async Task<string> AskAsync(string userText, CancellationToken ct = default)
    {
        _logger.LogInformation("Sending to OpenClaw: {Text}", userText);

        _history.Add(new RequestMessage("user", userText));

        var requestBody = new ResponsesRequest(_history);
        var json = JsonSerializer.Serialize(requestBody, OpenClawJsonContext.Default.ResponsesRequest);

        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        var url = _options.OpenClawBaseUrl.TrimEnd('/') + "/v1/responses";

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = content,
        };

        if (!string.IsNullOrWhiteSpace(_options.OpenClawBearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.OpenClawBearerToken);
        }

        var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var parsed = JsonSerializer.Deserialize(responseJson, OpenClawJsonContext.Default.ResponsesResponse);

        var assistantText = parsed?.OutputText ?? string.Empty;
        _logger.LogInformation("OpenClaw response: {Text}", assistantText);

        if (!string.IsNullOrWhiteSpace(assistantText))
        {
            _history.Add(new RequestMessage("assistant", assistantText));
        }

        return assistantText;
    }

    /// <summary>
    /// Clears conversation history.
    /// </summary>
    public void ClearHistory() => _history.Clear();
}

// ---- JSON models ----

internal sealed record RequestMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content);

internal sealed class ResponsesRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = "gpt-4o-mini";

    [JsonPropertyName("input")]
    public IReadOnlyList<RequestMessage> Input { get; }

    public ResponsesRequest(IReadOnlyList<RequestMessage> input)
    {
        Input = input;
    }
}

internal sealed class ResponsesResponse
{
    [JsonPropertyName("output_text")]
    public string? OutputText { get; set; }
}

[JsonSerializable(typeof(ResponsesRequest))]
[JsonSerializable(typeof(ResponsesResponse))]
[JsonSerializable(typeof(RequestMessage))]
internal partial class OpenClawJsonContext : JsonSerializerContext { }
