namespace ArkaneSystems.Raven.Core.AgentRuntime.Foundry;

// Strongly-typed configuration for the Foundry/Azure OpenAI connection.
// Bound from the "Foundry" section in appsettings.json / user-secrets.
// Sensitive values (Endpoint, DeploymentName) should be stored in user-secrets
// during development and in a Key Vault reference or environment variable in
// production — never committed to source control.
public class FoundryOptions
{
    public const string SectionName = "Foundry";

    // The Azure OpenAI endpoint URL, e.g. https://<resource>.openai.azure.com/
    public string Endpoint { get; set; } = string.Empty;

    // The logical name given to the agent when it is registered in Foundry.
    public string AgentName { get; set; } = "Raven";

    // The name of the model deployment to use (e.g. "gpt-4o-mini").
    public string DeploymentName { get; set; } = string.Empty;

    // The system prompt sent to the agent at the start of every conversation.
    // Override this in configuration to change Raven's persona or capabilities.
    public string SystemPrompt { get; set; } = "You are Raven, a helpful AI assistant.";
}
