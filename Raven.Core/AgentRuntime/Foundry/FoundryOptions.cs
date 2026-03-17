namespace ArkaneSystems.Raven.Core.AgentRuntime.Foundry;

public class FoundryOptions
{
    public const string SectionName = "Foundry";

    public string Endpoint { get; set; } = string.Empty;
    public string AgentName { get; set; } = "Raven";
    public string DeploymentName { get; set; } = string.Empty;
    public string SystemPrompt { get; set; } = "You are Raven, a helpful AI assistant.";
}
