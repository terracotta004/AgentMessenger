namespace AgentMessenger.ConsoleApp.Models;

public enum ChannelType
{
    Email,
    MauiMessenger
}

public sealed record Message(
    string Id,
    string From,
    string To,
    string Subject,
    string Body,
    DateTimeOffset SentAtUtc,
    ChannelType Channel,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record SendResult(bool Success, string? ErrorMessage = null);

public sealed record AgentInfo(
    string Id,
    string Identity,
    string DisplayName,
    string Email,
    string? Provider = null,
    string? Model = null,
    string? ApiKeyEnvironmentVariable = null);
