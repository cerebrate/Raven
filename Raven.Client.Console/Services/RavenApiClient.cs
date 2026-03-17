using System.Net.Http.Json;
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
}
