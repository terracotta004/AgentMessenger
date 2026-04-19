namespace AgentMessenger.ConsoleApp.Messaging;

public static class CliParser
{
    public static IReadOnlyDictionary<string, string> Parse(IReadOnlyList<string> args)
    {
        var output = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Count; i++)
        {
            var token = args[i];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var key = token[2..];
            if (i + 1 >= args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                output[key] = "true";
                continue;
            }

            output[key] = args[++i];
        }

        return output;
    }
}
