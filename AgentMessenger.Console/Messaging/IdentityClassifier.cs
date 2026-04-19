namespace AgentMessenger.ConsoleApp.Messaging;

public static class IdentityClassifier
{
    public static string GetType(string identity)
    {
        if (identity.Contains('@', StringComparison.Ordinal))
        {
            return "human";
        }

        if (identity.StartsWith("agent:", StringComparison.OrdinalIgnoreCase)
            || identity.StartsWith("bot:", StringComparison.OrdinalIgnoreCase))
        {
            return "agent";
        }

        return "unknown";
    }
}
