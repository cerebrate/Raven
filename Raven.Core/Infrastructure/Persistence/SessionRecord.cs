namespace ArkaneSystems.Raven.Core.Infrastructure.Persistence;

public class SessionRecord
{
    public string SessionId { get; set; } = string.Empty;
    public string ConversationId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastActivityAt { get; set; }
}
