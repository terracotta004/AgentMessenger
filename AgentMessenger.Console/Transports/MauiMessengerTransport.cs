using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AgentMessenger.ConsoleApp.Configuration;
using AgentMessenger.ConsoleApp.Models;

namespace AgentMessenger.ConsoleApp.Transports;

public sealed class MauiMessengerTransport : IMessageTransport
{
    private readonly HttpClient _httpClient;
    private readonly MauiMessengerConfig _config;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public MauiMessengerTransport(MauiMessengerConfig config)
    {
        _config = config;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(config.BaseUrl),
            Timeout = TimeSpan.FromSeconds(Math.Max(1, config.RequestTimeoutSeconds))
        };
        _httpClient.DefaultRequestHeaders.Add("X-AgentMessenger-Key", config.ApiKey);
    }

    public string BaseUrl => _config.BaseUrl;
    public string RegisterAgentPath => _config.RegisterAgentPath;

    public async Task<SendResult> SendAsync(Message message, CancellationToken cancellationToken)
    {
        var payload = new MauiMessageContract(
            message.Id,
            message.From,
            message.To,
            message.Subject,
            message.Body,
            message.SentAtUtc,
            message.Metadata);

        try
        {
            using var content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(_config.OutboundPath, content, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return new SendResult(true);
            }

            var details = await response.Content.ReadAsStringAsync(cancellationToken);
            return new SendResult(false, $"MauiMessenger returned {(int)response.StatusCode}: {details}");
        }
        catch (Exception ex)
        {
            return new SendResult(false, $"Could not reach MauiMessenger at {_config.BaseUrl}: {ex.Message}");
        }
    }

    public async Task<IReadOnlyList<Message>> GetInboxAsync(string owner, CancellationToken cancellationToken)
    {
        var path = _config.InboxPathTemplate.Replace("{identity}", Uri.EscapeDataString(owner), StringComparison.Ordinal);

        try
        {
            var results = await _httpClient.GetFromJsonAsync<List<MauiMessageContract>>(path, cancellationToken)
                ?? new List<MauiMessageContract>();

            return results.Select(c => new Message(
                c.Id,
                c.From,
                c.To,
                c.Subject,
                c.Body,
                c.SentAtUtc,
                ChannelType.MauiMessenger,
                c.Metadata)).ToArray();
        }
        catch
        {
            return Array.Empty<Message>();
        }
    }

    public async Task<SendResult> RegisterAgentAsync(
        string identity,
        string displayName,
        string? email,
        CancellationToken cancellationToken)
    {
        var payload = new RegisterAgentContract(identity, displayName, email);

        try
        {
            using var content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(_config.RegisterAgentPath, content, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return new SendResult(true);
            }

            var details = await response.Content.ReadAsStringAsync(cancellationToken);
            return new SendResult(false, $"MauiMessenger returned {(int)response.StatusCode}: {details}");
        }
        catch (Exception ex)
        {
            return new SendResult(false, $"Could not register agent in MauiMessenger at {_config.BaseUrl}: {ex.Message}");
        }
    }

    public async Task<IReadOnlyList<AgentInfo>> ListAgentsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var results = await _httpClient.GetFromJsonAsync<List<MauiUserContract>>(_config.AgentsPath, cancellationToken)
                ?? new List<MauiUserContract>();

            return results
                .Select(user => new AgentInfo(user.Id, user.Username, user.DisplayName, user.Email))
                .ToArray();
        }
        catch
        {
            return Array.Empty<AgentInfo>();
        }
    }

    private sealed record MauiMessageContract(
        string Id,
        string From,
        string To,
        string Subject,
        string Body,
        DateTimeOffset SentAtUtc,
        IReadOnlyDictionary<string, string> Metadata);

    private sealed record RegisterAgentContract(
        string Identity,
        string DisplayName,
        string? Email);

    private sealed record MauiUserContract(
        string Id,
        string Username,
        string DisplayName,
        string Email,
        int ParticipantType,
        DateTime CreatedAt,
        DateTime UpdatedAt);
}
