using AgentMessenger.ConsoleApp.Configuration;
using AgentMessenger.ConsoleApp.Messaging;
using AgentMessenger.ConsoleApp.Models;
using AgentMessenger.ConsoleApp.Transports;

var appConfig = AppConfig.Load();
var router = BuildRouter(appConfig);

if (args.Length == 0)
{
    PrintHelp();
    return;
}

var command = args[0].Trim().ToLowerInvariant();

switch (command)
{
    case "send":
        await SendAsync(args.Skip(1).ToArray(), router, appConfig);
        break;
    case "inbox":
        await InboxAsync(args.Skip(1).ToArray(), router);
        break;
    case "help":
    case "--help":
    case "-h":
        PrintHelp();
        break;
    default:
        Console.WriteLine($"Unknown command '{command}'.");
        PrintHelp();
        break;
}

return;

static MessageRouter BuildRouter(AppConfig appConfig)
{
    var transports = new Dictionary<ChannelType, IMessageTransport>
    {
        [ChannelType.Email] = new EmailTransport(appConfig.Email),
        [ChannelType.MauiMessenger] = new MauiMessengerTransport(appConfig.MauiMessenger)
    };

    return new MessageRouter(transports);
}

static async Task SendAsync(string[] args, MessageRouter router, AppConfig config)
{
    var options = CliParser.Parse(args);

    if (!options.TryGetValue("from", out var from)
        || !options.TryGetValue("to", out var to)
        || !options.TryGetValue("channel", out var channelRaw)
        || !options.TryGetValue("subject", out var subject)
        || !options.TryGetValue("body", out var body))
    {
        Console.WriteLine("Missing required options for send command.");
        Console.WriteLine("Required: --from --to --channel --subject --body");
        return;
    }

    if (!Enum.TryParse<ChannelType>(channelRaw, ignoreCase: true, out var channel))
    {
        Console.WriteLine($"Unsupported channel '{channelRaw}'. Supported: Email, MauiMessenger");
        return;
    }

    var message = new Message(
        Id: Guid.NewGuid().ToString("N"),
        From: from,
        To: to,
        Subject: subject,
        Body: body,
        SentAtUtc: DateTimeOffset.UtcNow,
        Channel: channel,
        Metadata: new Dictionary<string, string>
        {
            ["senderType"] = IdentityClassifier.GetType(from),
            ["recipientType"] = IdentityClassifier.GetType(to),
            ["mauiBridgeRequired"] = (channel == ChannelType.MauiMessenger).ToString()
        });

    var result = await router.SendAsync(message, CancellationToken.None);

    Console.WriteLine(result.Success
        ? $"Sent message {message.Id} using {channel}."
        : $"Failed to send message {message.Id}: {result.ErrorMessage}");

    if (channel == ChannelType.MauiMessenger)
    {
        Console.WriteLine($"MauiMessenger base URL: {config.MauiMessenger.BaseUrl}");
        Console.WriteLine("Update MauiMessenger to add AgentMessenger endpoint compatibility if not already available.");
    }
}

static async Task InboxAsync(string[] args, MessageRouter router)
{
    var options = CliParser.Parse(args);
    if (!options.TryGetValue("owner", out var owner)
        || !options.TryGetValue("channel", out var channelRaw))
    {
        Console.WriteLine("Missing required options for inbox command.");
        Console.WriteLine("Required: --owner --channel");
        return;
    }

    if (!Enum.TryParse<ChannelType>(channelRaw, ignoreCase: true, out var channel))
    {
        Console.WriteLine($"Unsupported channel '{channelRaw}'. Supported: Email, MauiMessenger");
        return;
    }

    var messages = await router.GetInboxAsync(owner, channel, CancellationToken.None);
    if (messages.Count == 0)
    {
        Console.WriteLine("No messages.");
        return;
    }

    foreach (var message in messages.OrderByDescending(m => m.SentAtUtc))
    {
        Console.WriteLine($"[{message.SentAtUtc:u}] {message.From} -> {message.To} | {message.Subject}");
        Console.WriteLine(message.Body);
        Console.WriteLine(new string('-', 80));
    }
}

static void PrintHelp()
{
    Console.WriteLine("AgentMessenger Console");
    Console.WriteLine();
    Console.WriteLine("Commands:");
    Console.WriteLine("  send  --from <identity> --to <identity> --channel <Email|MauiMessenger> --subject <text> --body <text>");
    Console.WriteLine("  inbox --owner <identity> --channel <Email|MauiMessenger>");
    Console.WriteLine();
    Console.WriteLine("Identities can be humans (email-like) or agents (agent:planner, bot:support). ");
}
