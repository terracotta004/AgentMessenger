using AgentMessenger.ConsoleApp.Models;

namespace AgentMessenger.ConsoleApp.Transports;

public interface IMessageTransport
{
    Task<SendResult> SendAsync(Message message, CancellationToken cancellationToken);
    Task<IReadOnlyList<Message>> GetInboxAsync(string owner, CancellationToken cancellationToken);
}
