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

    public MauiMessengerTransport(MauiMessengerConfig config)
    {
        _config = config;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(config.BaseUrl)
        };
        _httpClient.DefaultRequestHeaders.Add("X-AgentMessenger-Key", config.ApiKey);
    }

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
            using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
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

    private sealed record MauiMessageContract(
        string Id,
        string From,
        string To,
        string Subject,
        string Body,
        DateTimeOffset SentAtUtc,
        IReadOnlyDictionary<string, string> Metadata);
}
