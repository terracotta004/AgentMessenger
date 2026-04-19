using AgentMessenger.ConsoleApp.Configuration;
using AgentMessenger.ConsoleApp.Models;

namespace AgentMessenger.ConsoleApp.Transports;

public sealed class EmailTransport : IMessageTransport
{
    private static readonly List<Message> Inbox = new();
    private static readonly object SyncRoot = new();
    private readonly EmailConfig _config;

    public EmailTransport(EmailConfig config)
    {
        _config = config;
    }

    public Task<SendResult> SendAsync(Message message, CancellationToken cancellationToken)
    {
        var from = string.IsNullOrWhiteSpace(message.From) ? _config.FromFallback : message.From;
        var normalized = message with { From = from };

        lock (SyncRoot)
        {
            Inbox.Add(normalized);
        }

        return Task.FromResult(new SendResult(true));
    }

    public Task<IReadOnlyList<Message>> GetInboxAsync(string owner, CancellationToken cancellationToken)
    {
        lock (SyncRoot)
        {
            var messages = Inbox.Where(m => string.Equals(m.To, owner, StringComparison.OrdinalIgnoreCase)).ToArray();
            return Task.FromResult<IReadOnlyList<Message>>(messages);
        }
    }
}
