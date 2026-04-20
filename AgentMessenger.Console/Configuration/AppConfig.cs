using System.Text.Json;

namespace AgentMessenger.ConsoleApp.Configuration;

public sealed class AppConfig
{
    public EmailConfig Email { get; init; } = new();
    public MauiMessengerConfig MauiMessenger { get; init; } = new();
    public List<AgentConfig> Agents { get; init; } = new();

    public static AppConfig Load()
    {
        var configPath = GetConfigPath();

        if (!File.Exists(configPath))
        {
            return new AppConfig();
        }

        var json = File.ReadAllText(configPath);
        return JsonSerializer.Deserialize<AppConfig>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new AppConfig();
    }

    public void Save()
    {
        var configPath = GetConfigPath();
        var directory = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        File.WriteAllText(configPath, json);
    }

    public AgentConfig? FindAgent(string identity)
    {
        return Agents.FirstOrDefault(agent =>
            string.Equals(agent.Identity, identity, StringComparison.OrdinalIgnoreCase));
    }

    public static string GetConfigPath()
    {
        var configuredPath = Environment.GetEnvironmentVariable("AGENT_MESSENGER_CONFIG");
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return configuredPath;
        }

        var candidates = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "AgentMessenger.Console", "appsettings.json"),
            Path.Combine(AppContext.BaseDirectory, "appsettings.json")
        };

        return candidates.FirstOrDefault(File.Exists) ?? candidates[^1];
    }
}

public sealed class EmailConfig
{
    public string SmtpHost { get; init; } = "localhost";
    public int SmtpPort { get; init; } = 25;
    public string FromFallback { get; init; } = "agentmessenger@localhost";
}

public sealed class MauiMessengerConfig
{
    public string BaseUrl { get; init; } = "http://localhost:5010";
    public string ApiKey { get; init; } = "dev-agentmessenger-key";
    public string OutboundPath { get; init; } = "/api/messages";
    public string InboxPathTemplate { get; init; } = "/api/messages/inbox/{identity}";
    public string AgentsPath { get; init; } = "/api/agents";
    public string RegisterAgentPath { get; init; } = "/api/agents/register";
    public int RequestTimeoutSeconds { get; init; } = 15;
}

public sealed class AgentConfig
{
    public string Identity { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string SystemPrompt { get; init; } = string.Empty;
    public string ApiKeyEnvironmentVariable { get; init; } = string.Empty;
}
