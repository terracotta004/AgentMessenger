using AgentMessenger.ConsoleApp.Models;
using AgentMessenger.ConsoleApp.Transports;

namespace AgentMessenger.ConsoleApp.Messaging;

public sealed class MessageRouter
{
    private readonly IReadOnlyDictionary<ChannelType, IMessageTransport> _transports;

    public MessageRouter(IReadOnlyDictionary<ChannelType, IMessageTransport> transports)
    {
        _transports = transports;
    }

    public async Task<SendResult> SendAsync(Message message, CancellationToken cancellationToken)
    {
        if (!_transports.TryGetValue(message.Channel, out var transport))
        {
            return new SendResult(false, $"No transport is configured for channel '{message.Channel}'.");
        }

        return await transport.SendAsync(message, cancellationToken);
    }

    public async Task<IReadOnlyList<Message>> GetInboxAsync(
        string owner,
        ChannelType channel,
        CancellationToken cancellationToken)
    {
        if (!_transports.TryGetValue(channel, out var transport))
        {
            return Array.Empty<Message>();
        }

        return await transport.GetInboxAsync(owner, cancellationToken);
    }
}
