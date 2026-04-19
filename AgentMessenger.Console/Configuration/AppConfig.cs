using System.Text.Json;

namespace AgentMessenger.ConsoleApp.Configuration;

public sealed class AppConfig
{
    public EmailConfig Email { get; init; } = new();
    public MauiMessengerConfig MauiMessenger { get; init; } = new();

    public static AppConfig Load()
    {
        var configPath = Environment.GetEnvironmentVariable("AGENT_MESSENGER_CONFIG")
            ?? Path.Combine(AppContext.BaseDirectory, "appsettings.json");

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
}
