using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AgentMessenger.ConsoleApp.Configuration;
using AgentMessenger.ConsoleApp.Models;

namespace AgentMessenger.ConsoleApp.Agents;

public sealed class GeminiTextClient : IAgentTextClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    public GeminiTextClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient
        {
            BaseAddress = new Uri("https://generativelanguage.googleapis.com")
        };
    }

    public async Task<string> GenerateReplyAsync(
        AgentConfig agent,
        Message? incomingMessage,
        string prompt,
        CancellationToken cancellationToken)
    {
        var apiKey = GetApiKey(agent);
        var model = agent.Model.StartsWith("models/", StringComparison.OrdinalIgnoreCase)
            ? agent.Model
            : $"models/{agent.Model}";
        var path = $"/v1beta/{model}:generateContent";

        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add("x-goog-api-key", apiKey);
        request.Content = JsonContent.Create(new
        {
            system_instruction = new
            {
                parts = new[]
                {
                    new { text = agent.SystemPrompt }
                }
            },
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new[]
                    {
                        new { text = BuildPrompt(agent, incomingMessage, prompt) }
                    }
                }
            }
        }, options: JsonOptions);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Gemini returned {(int)response.StatusCode}: {json}");
        }

        return ExtractText(json);
    }

    private static string GetApiKey(AgentConfig agent)
    {
        var envVar = string.IsNullOrWhiteSpace(agent.ApiKeyEnvironmentVariable)
            ? "GEMINI_API_KEY"
            : agent.ApiKeyEnvironmentVariable;
        var apiKey = Environment.GetEnvironmentVariable(envVar);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException($"{envVar} is not set for {agent.Identity}.");
        }

        return apiKey;
    }

    private static string BuildPrompt(AgentConfig agent, Message? incomingMessage, string prompt)
    {
        if (incomingMessage is null)
        {
            return prompt;
        }

        var builder = new StringBuilder();
        builder.AppendLine($"You are {agent.Identity} replying in an agent-to-agent conversation.");
        builder.AppendLine($"Incoming message from {incomingMessage.From} to {incomingMessage.To}.");
        builder.AppendLine($"Subject: {incomingMessage.Subject}");
        builder.AppendLine("Body:");
        builder.AppendLine(incomingMessage.Body);
        builder.AppendLine();
        builder.AppendLine(prompt);
        return builder.ToString();
    }

    private static string ExtractText(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("candidates", out var candidates)
            || candidates.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Gemini response did not contain candidates.");
        }

        foreach (var candidate in candidates.EnumerateArray())
        {
            if (!candidate.TryGetProperty("content", out var content)
                || !content.TryGetProperty("parts", out var parts)
                || parts.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("text", out var text)
                    && text.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(text.GetString()))
                {
                    return text.GetString()!.Trim();
                }
            }
        }

        throw new InvalidOperationException("Gemini response did not contain text output.");
    }
}
