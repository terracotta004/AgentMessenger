using AgentMessenger.ConsoleApp.Configuration;
using AgentMessenger.ConsoleApp.Models;

namespace AgentMessenger.ConsoleApp.Agents;

public interface IAgentTextClient
{
    Task<string> GenerateReplyAsync(
        AgentConfig agent,
        Message? incomingMessage,
        string prompt,
        CancellationToken cancellationToken);
}
