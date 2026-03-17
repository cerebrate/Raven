using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using ArkaneSystems.Raven.Contracts.Chat;

namespace ArkaneSystems.Raven.Client.Console.Services;

public class RavenApiClient(HttpClient http)
{
    public async Task<string> CreateSessionAsync()
    {
        var response = await http.PostAsJsonAsync("/api/chat/sessions", new CreateSessionRequest());
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<CreateSessionResponse>();
        return result!.SessionId;
    }

    public async Task<string> SendMessageAsync(string sessionId, string content)
    {
        var response = await http.PostAsJsonAsync(
            $"/api/chat/sessions/{sessionId}/messages",
            new SendMessageRequest(content));
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<SendMessageResponse>();
        return result!.Content;
    }

    public async IAsyncEnumerable<string> StreamMessageAsync(
        string sessionId,
        string content,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/chat/sessions/{sessionId}/messages/stream")
        {
            Content = JsonContent.Create(new SendMessageRequest(content))
        };

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);

            if (line is null)
                break;

            if (!line.StartsWith("data: ", StringComparison.Ordinal))
                continue;

            var chunk = line["data: ".Length..];
            if (!string.IsNullOrEmpty(chunk))
                yield return chunk;
        }
    }
}
