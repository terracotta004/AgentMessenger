using AgentMessenger.ConsoleApp.Agents;
using AgentMessenger.ConsoleApp.Configuration;
using AgentMessenger.ConsoleApp.Messaging;
using AgentMessenger.ConsoleApp.Models;
using AgentMessenger.ConsoleApp.Transports;

EnvFileLoader.LoadNearest();

var appConfig = AppConfig.Load();
var mauiMessengerTransport = new MauiMessengerTransport(appConfig.MauiMessenger);
var router = BuildRouter(appConfig, mauiMessengerTransport);
var agentClients = BuildAgentClients();

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
    case "converse":
        await ConverseAsync(args.Skip(1).ToArray(), router, appConfig, agentClients);
        break;
    case "agents":
        await AgentsAsync(args.Skip(1).ToArray(), mauiMessengerTransport, appConfig);
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

static MessageRouter BuildRouter(AppConfig appConfig, MauiMessengerTransport mauiMessengerTransport)
{
    var transports = new Dictionary<ChannelType, IMessageTransport>
    {
        [ChannelType.Email] = new EmailTransport(appConfig.Email),
        [ChannelType.MauiMessenger] = mauiMessengerTransport
    };

    return new MessageRouter(transports);
}

static IReadOnlyDictionary<string, IAgentTextClient> BuildAgentClients()
    => new Dictionary<string, IAgentTextClient>(StringComparer.OrdinalIgnoreCase)
    {
        ["OpenAI"] = new OpenAiTextClient(),
        ["Gemini"] = new GeminiTextClient()
    };

static async Task AgentsAsync(
    string[] args,
    MauiMessengerTransport mauiMessengerTransport,
    AppConfig appConfig)
{
    if (args.Length == 0)
    {
        Console.WriteLine("Missing agents subcommand.");
        PrintAgentsHelp();
        return;
    }

    var subcommand = args[0].Trim().ToLowerInvariant();
    switch (subcommand)
    {
        case "list":
            await ListAgentsAsync(mauiMessengerTransport, appConfig);
            break;
        case "register":
            await RegisterAgentAsync(args.Skip(1).ToArray(), mauiMessengerTransport, appConfig);
            break;
        case "update":
            await UpdateAgentAsync(args.Skip(1).ToArray(), mauiMessengerTransport, appConfig);
            break;
        case "sync":
            await SyncAgentAsync(args.Skip(1).ToArray(), mauiMessengerTransport, appConfig);
            break;
        case "check-key":
            CheckAgentKey(args.Skip(1).ToArray(), appConfig);
            break;
        default:
            Console.WriteLine($"Unknown agents subcommand '{subcommand}'.");
            PrintAgentsHelp();
            break;
    }
}

static void CheckAgentKey(string[] args, AppConfig appConfig)
{
    var options = CliParser.Parse(args);

    if (!options.TryGetValue("identity", out var identity))
    {
        Console.WriteLine("Missing required option for agents check-key.");
        Console.WriteLine("Required: --identity <agent:id>");
        return;
    }

    var agent = appConfig.FindAgent(identity);
    if (agent is null)
    {
        Console.WriteLine($"No local agent config found for {identity}.");
        return;
    }

    if (string.IsNullOrWhiteSpace(agent.ApiKeyEnvironmentVariable))
    {
        Console.WriteLine($"{identity} does not have an API key environment variable configured.");
        return;
    }

    var value = Environment.GetEnvironmentVariable(agent.ApiKeyEnvironmentVariable);
    Console.WriteLine(string.IsNullOrWhiteSpace(value)
        ? $"{agent.ApiKeyEnvironmentVariable} is not set for {identity}."
        : $"{agent.ApiKeyEnvironmentVariable} is set for {identity}.");
}

static async Task ListAgentsAsync(MauiMessengerTransport mauiMessengerTransport, AppConfig appConfig)
{
    var mauiAgents = await mauiMessengerTransport.ListAgentsAsync(CancellationToken.None);
    var agents = MergeAgentLists(mauiAgents, appConfig.Agents);
    if (agents.Count == 0)
    {
        Console.WriteLine("No registered agents.");
        return;
    }

    foreach (var agent in agents.OrderBy(agent => agent.Identity))
    {
        var provider = string.IsNullOrWhiteSpace(agent.Provider) ? "unconfigured" : agent.Provider;
        var model = string.IsNullOrWhiteSpace(agent.Model) ? "unconfigured" : agent.Model;
        var apiKeyEnv = string.IsNullOrWhiteSpace(agent.ApiKeyEnvironmentVariable)
            ? "unconfigured"
            : agent.ApiKeyEnvironmentVariable;

        Console.WriteLine($"{agent.Identity} | {agent.DisplayName} | {provider} | {model} | key env: {apiKeyEnv} | {agent.Email} | {agent.Id}");
    }
}

static async Task RegisterAgentAsync(
    string[] args,
    MauiMessengerTransport mauiMessengerTransport,
    AppConfig appConfig)
{
    var options = CliParser.Parse(args);

    if (!options.TryGetValue("identity", out var identity))
    {
        Console.WriteLine("Missing required option for agents register.");
        Console.WriteLine("Required: --identity <agent:id>");
        return;
    }

    if (!IdentityClassifier.GetType(identity).Equals("agent", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine("Agent identities must start with agent: or bot:.");
        return;
    }

    if (!options.TryGetValue("provider", out var providerRaw)
        || !TryNormalizeAgentProvider(providerRaw, out var provider))
    {
        Console.WriteLine("Missing or unsupported provider. Supported providers: OpenAI, ChatGPT, Gemini.");
        return;
    }

    if (!options.TryGetValue("model", out var model)
        || string.IsNullOrWhiteSpace(model))
    {
        Console.WriteLine("Missing required option for agents register.");
        Console.WriteLine("Required: --model <model>");
        return;
    }

    if (!options.TryGetValue("system-prompt", out var systemPrompt)
        || string.IsNullOrWhiteSpace(systemPrompt))
    {
        Console.WriteLine("Missing required option for agents register.");
        Console.WriteLine("Required: --system-prompt <text>");
        return;
    }

    options.TryGetValue("display-name", out var displayName);
    options.TryGetValue("email", out var email);
    options.TryGetValue("api-key-env", out var apiKeyEnv);

    displayName = string.IsNullOrWhiteSpace(displayName) ? identity : displayName.Trim();
    apiKeyEnv = string.IsNullOrWhiteSpace(apiKeyEnv)
        ? GetDefaultApiKeyEnvironmentVariable(provider)
        : apiKeyEnv.Trim();

    UpsertAgentConfig(
        appConfig,
        new AgentConfig
        {
            Identity = identity.Trim(),
            DisplayName = displayName,
            Provider = provider,
            Model = model.Trim(),
            SystemPrompt = systemPrompt.Trim(),
            ApiKeyEnvironmentVariable = apiKeyEnv
        });

    appConfig.Save();

    Console.WriteLine($"Registered agent {identity} with {provider} model {model.Trim()}.");
    Console.WriteLine($"Local agent config saved to {AppConfig.GetConfigPath()}.");

    var result = await mauiMessengerTransport.RegisterAgentAsync(
        identity,
        displayName,
        email,
        CancellationToken.None);

    Console.WriteLine(result.Success
        ? "MauiMessenger identity is registered."
        : $"MauiMessenger identity was not registered: {result.ErrorMessage}");
}

static async Task UpdateAgentAsync(
    string[] args,
    MauiMessengerTransport mauiMessengerTransport,
    AppConfig appConfig)
{
    var options = CliParser.Parse(args);

    if (!options.TryGetValue("identity", out var identity))
    {
        Console.WriteLine("Missing required option for agents update.");
        Console.WriteLine("Required: --identity <agent:id>");
        return;
    }

    var existing = appConfig.FindAgent(identity);
    if (existing is null)
    {
        Console.WriteLine($"No local agent config found for {identity}.");
        return;
    }

    var newIdentity = options.TryGetValue("new-identity", out var newIdentityRaw)
        ? newIdentityRaw.Trim()
        : existing.Identity;

    if (!IdentityClassifier.GetType(newIdentity).Equals("agent", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine("Agent identities must start with agent: or bot:.");
        return;
    }

    var duplicate = appConfig.FindAgent(newIdentity);
    if (duplicate is not null && !string.Equals(existing.Identity, newIdentity, StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine($"A local agent config already exists for {newIdentity}.");
        return;
    }

    var provider = existing.Provider;
    if (options.TryGetValue("provider", out var providerRaw)
        && !TryNormalizeAgentProvider(providerRaw, out provider))
    {
        Console.WriteLine("Unsupported provider. Supported providers: OpenAI, ChatGPT, Gemini.");
        return;
    }

    var updated = new AgentConfig
    {
        Identity = newIdentity,
        DisplayName = GetOptionOrExisting(options, "display-name", existing.DisplayName),
        Provider = provider,
        Model = GetOptionOrExisting(options, "model", existing.Model),
        SystemPrompt = GetOptionOrExisting(options, "system-prompt", existing.SystemPrompt),
        ApiKeyEnvironmentVariable = GetOptionOrExisting(
            options,
            "api-key-env",
            string.IsNullOrWhiteSpace(existing.ApiKeyEnvironmentVariable)
                ? GetDefaultApiKeyEnvironmentVariable(provider)
                : existing.ApiKeyEnvironmentVariable)
    };

    appConfig.Agents.Remove(existing);
    UpsertAgentConfig(appConfig, updated);
    appConfig.Save();

    Console.WriteLine($"Updated local agent config for {existing.Identity}.");
    if (!string.Equals(existing.Identity, updated.Identity, StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine($"Local identity changed to {updated.Identity}.");
    }

    if (options.ContainsKey("sync-maui"))
    {
        await RegisterMauiAgentIdentityAsync(updated, mauiMessengerTransport, options);
    }
    else
    {
        Console.WriteLine("Run agents sync --identity " + updated.Identity + " to register this identity with MauiMessenger.");
    }
}

static async Task SyncAgentAsync(
    string[] args,
    MauiMessengerTransport mauiMessengerTransport,
    AppConfig appConfig)
{
    var options = CliParser.Parse(args);

    if (!options.TryGetValue("identity", out var identity))
    {
        Console.WriteLine("Missing required option for agents sync.");
        Console.WriteLine("Required: --identity <agent:id>");
        return;
    }

    var agent = appConfig.FindAgent(identity);
    if (agent is null)
    {
        Console.WriteLine($"No local agent config found for {identity}.");
        return;
    }

    await RegisterMauiAgentIdentityAsync(agent, mauiMessengerTransport, options);
}

static async Task RegisterMauiAgentIdentityAsync(
    AgentConfig agent,
    MauiMessengerTransport mauiMessengerTransport,
    IReadOnlyDictionary<string, string> options)
{
    options.TryGetValue("email", out var email);
    var apiKeyEnv = string.IsNullOrWhiteSpace(agent.ApiKeyEnvironmentVariable)
        ? "unconfigured"
        : agent.ApiKeyEnvironmentVariable;
    var apiKeyStatus = string.IsNullOrWhiteSpace(agent.ApiKeyEnvironmentVariable)
        ? "not checked"
        : string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(agent.ApiKeyEnvironmentVariable))
            ? "not set"
            : "set";

    Console.WriteLine("Syncing local agent identity to MauiMessenger.");
    Console.WriteLine($"Config file: {AppConfig.GetConfigPath()}");
    Console.WriteLine($"Identity: {agent.Identity}");
    Console.WriteLine($"Display name: {(string.IsNullOrWhiteSpace(agent.DisplayName) ? agent.Identity : agent.DisplayName)}");
    Console.WriteLine($"Provider/model: {agent.Provider} / {agent.Model}");
    Console.WriteLine($"API key env var: {apiKeyEnv} ({apiKeyStatus}; key value is not sent to MauiMessenger)");
    Console.WriteLine($"MauiMessenger request: POST {mauiMessengerTransport.BaseUrl.TrimEnd('/')}{mauiMessengerTransport.RegisterAgentPath}");
    Console.WriteLine(string.IsNullOrWhiteSpace(email)
        ? "Email: omitted; MauiMessenger will assign an internal agent email if needed."
        : $"Email: {email}");
    Console.WriteLine("This command only ensures the MauiMessenger user identity exists. It does not call OpenAI or Gemini.");
    Console.WriteLine("Sending request...");

    var result = await mauiMessengerTransport.RegisterAgentAsync(
        agent.Identity,
        string.IsNullOrWhiteSpace(agent.DisplayName) ? agent.Identity : agent.DisplayName,
        email,
        CancellationToken.None);

    Console.WriteLine(result.Success
        ? $"Done. MauiMessenger identity is registered for {agent.Identity}."
        : $"MauiMessenger identity was not registered for {agent.Identity}: {result.ErrorMessage}");
}

static IReadOnlyList<AgentInfo> MergeAgentLists(
    IReadOnlyList<AgentInfo> mauiAgents,
    IReadOnlyList<AgentConfig> configuredAgents)
{
    var output = mauiAgents.ToDictionary(agent => agent.Identity, StringComparer.OrdinalIgnoreCase);

    foreach (var configured in configuredAgents)
    {
        if (output.TryGetValue(configured.Identity, out var mauiAgent))
        {
            output[configured.Identity] = mauiAgent with
            {
                DisplayName = string.IsNullOrWhiteSpace(mauiAgent.DisplayName)
                    ? configured.DisplayName
                    : mauiAgent.DisplayName,
                Provider = configured.Provider,
                Model = configured.Model,
                ApiKeyEnvironmentVariable = configured.ApiKeyEnvironmentVariable
            };
            continue;
        }

        output[configured.Identity] = new AgentInfo(
            "local",
            configured.Identity,
            configured.DisplayName,
            string.Empty,
            configured.Provider,
            configured.Model,
            configured.ApiKeyEnvironmentVariable);
    }

    return output.Values.ToArray();
}

static void UpsertAgentConfig(AppConfig appConfig, AgentConfig agentConfig)
{
    var existing = appConfig.FindAgent(agentConfig.Identity);
    if (existing is not null)
    {
        appConfig.Agents.Remove(existing);
    }

    appConfig.Agents.Add(agentConfig);
}

static string GetOptionOrExisting(
    IReadOnlyDictionary<string, string> options,
    string key,
    string existing)
{
    return options.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
        ? value.Trim()
        : existing;
}

static bool TryNormalizeAgentProvider(string providerRaw, out string provider)
{
    provider = providerRaw.Trim();

    if (provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase)
        || provider.Equals("ChatGPT", StringComparison.OrdinalIgnoreCase))
    {
        provider = "OpenAI";
        return true;
    }

    if (provider.Equals("Gemini", StringComparison.OrdinalIgnoreCase)
        || provider.Equals("GoogleGemini", StringComparison.OrdinalIgnoreCase)
        || provider.Equals("Google Gemini", StringComparison.OrdinalIgnoreCase))
    {
        provider = "Gemini";
        return true;
    }

    return false;
}

static string GetDefaultApiKeyEnvironmentVariable(string provider)
    => provider.Equals("Gemini", StringComparison.OrdinalIgnoreCase)
        ? "GEMINI_API_KEY"
        : "OPENAI_API_KEY";

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
    }
}

static async Task ConverseAsync(
    string[] args,
    MessageRouter router,
    AppConfig config,
    IReadOnlyDictionary<string, IAgentTextClient> agentClients)
{
    var options = CliParser.Parse(args);

    if (!options.TryGetValue("from", out var from)
        || !options.TryGetValue("to", out var to))
    {
        Console.WriteLine("Missing required options for converse command.");
        Console.WriteLine("Required: --from --to");
        return;
    }

    var channelRaw = options.TryGetValue("channel", out var configuredChannel)
        ? configuredChannel
        : nameof(ChannelType.MauiMessenger);
    if (!Enum.TryParse<ChannelType>(channelRaw, ignoreCase: true, out var channel))
    {
        Console.WriteLine($"Unsupported channel '{channelRaw}'. Supported: Email, MauiMessenger");
        return;
    }

    var subject = options.TryGetValue("subject", out var configuredSubject)
        ? configuredSubject
        : "Agent introduction";
    var openingPrompt = options.TryGetValue("prompt", out var configuredPrompt)
        ? configuredPrompt
        : "Send a short friendly introduction to the other agent. Invite them to reply, and keep it under 80 words.";
    var replyPrompt = options.TryGetValue("reply-prompt", out var configuredReplyPrompt)
        ? configuredReplyPrompt
        : "Reply to the message as yourself. Keep the exchange moving with one concrete question or next step, and keep it under 120 words.";

    var turns = GetIntOption(options, "turns", 4, minimum: 1);
    var delaySeconds = GetIntOption(options, "delay-seconds", 5, minimum: 0);

    var sender = config.FindAgent(from);
    var recipient = config.FindAgent(to);
    if (sender is null || recipient is null)
    {
        Console.WriteLine(sender is null
            ? $"No local agent config found for {from}."
            : $"No local agent config found for {to}.");
        return;
    }

    Message? previousMessage = null;
    for (var turn = 1; turn <= turns; turn++)
    {
        var prompt = previousMessage is null ? openingPrompt : replyPrompt;
        string body;
        try
        {
            body = await GenerateAgentTextAsync(sender, previousMessage, prompt, agentClients, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not generate response for {sender.Identity}: {ex.Message}");
            return;
        }

        var message = new Message(
            Id: Guid.NewGuid().ToString("N"),
            From: sender.Identity,
            To: recipient.Identity,
            Subject: subject,
            Body: body,
            SentAtUtc: DateTimeOffset.UtcNow,
            Channel: channel,
            Metadata: new Dictionary<string, string>
            {
                ["senderType"] = IdentityClassifier.GetType(sender.Identity),
                ["recipientType"] = IdentityClassifier.GetType(recipient.Identity),
                ["turn"] = turn.ToString(),
                ["conversation"] = "true"
            });

        Console.WriteLine($"Turn {turn}/{turns}: {sender.Identity} -> {recipient.Identity}");
        Console.WriteLine(body);
        Console.WriteLine(new string('-', 80));

        var result = await router.SendAsync(message, CancellationToken.None);
        if (!result.Success)
        {
            Console.WriteLine($"Failed to send message {message.Id}: {result.ErrorMessage}");
            return;
        }

        previousMessage = message;
        (sender, recipient) = (recipient, sender);

        if (turn < turns && delaySeconds > 0)
        {
            Console.WriteLine($"Waiting {delaySeconds}s before next response...");
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
        }
    }

    if (channel == ChannelType.MauiMessenger)
    {
        Console.WriteLine($"MauiMessenger base URL: {config.MauiMessenger.BaseUrl}");
    }
}

static async Task<string> GenerateAgentTextAsync(
    AgentConfig agent,
    Message? incomingMessage,
    string prompt,
    IReadOnlyDictionary<string, IAgentTextClient> agentClients,
    CancellationToken cancellationToken)
{
    if (!agentClients.TryGetValue(agent.Provider, out var client))
    {
        throw new InvalidOperationException($"Unsupported provider '{agent.Provider}' for {agent.Identity}.");
    }

    return await client.GenerateReplyAsync(agent, incomingMessage, prompt, cancellationToken);
}

static int GetIntOption(
    IReadOnlyDictionary<string, string> options,
    string key,
    int defaultValue,
    int minimum)
{
    if (!options.TryGetValue(key, out var rawValue))
    {
        return defaultValue;
    }

    if (!int.TryParse(rawValue, out var value))
    {
        return defaultValue;
    }

    return Math.Max(minimum, value);
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
    Console.WriteLine("  converse --from <agent:id> --to <agent:id> [--channel <MauiMessenger>] [--turns <count>] [--delay-seconds <seconds>] [--prompt <text>] [--reply-prompt <text>]");
    Console.WriteLine("  agents list");
    Console.WriteLine("  agents register --identity <agent:id> --display-name <name> --provider <OpenAI|ChatGPT|Gemini> --model <model> --system-prompt <text> [--api-key-env <name>] [--email <address>]");
    Console.WriteLine("  agents update --identity <agent:id> [--new-identity <agent:id>] [--display-name <name>] [--provider <OpenAI|ChatGPT|Gemini>] [--model <model>] [--system-prompt <text>] [--api-key-env <name>] [--sync-maui]");
    Console.WriteLine("  agents sync --identity <agent:id> [--email <address>]");
    Console.WriteLine("  agents check-key --identity <agent:id>");
    Console.WriteLine();
    Console.WriteLine("Identities can be humans (email-like) or agents (agent:planner, bot:support). ");
}

static void PrintAgentsHelp()
{
    Console.WriteLine("Supported agent commands:");
    Console.WriteLine("  agents list");
    Console.WriteLine("  agents register --identity <agent:id> --display-name <name> --provider <OpenAI|ChatGPT|Gemini> --model <model> --system-prompt <text> [--api-key-env <name>] [--email <address>]");
    Console.WriteLine("  agents update --identity <agent:id> [--new-identity <agent:id>] [--display-name <name>] [--provider <OpenAI|ChatGPT|Gemini>] [--model <model>] [--system-prompt <text>] [--api-key-env <name>] [--sync-maui]");
    Console.WriteLine("  agents sync --identity <agent:id> [--email <address>]");
    Console.WriteLine("  agents check-key --identity <agent:id>");
}
