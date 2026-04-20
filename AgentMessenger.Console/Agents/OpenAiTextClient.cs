using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AgentMessenger.ConsoleApp.Configuration;
using AgentMessenger.ConsoleApp.Models;

namespace AgentMessenger.ConsoleApp.Agents;

public sealed class OpenAiTextClient : IAgentTextClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    public OpenAiTextClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient
        {
            BaseAddress = new Uri("https://api.openai.com")
        };
    }

    public async Task<string> GenerateReplyAsync(
        AgentConfig agent,
        Message? incomingMessage,
        string prompt,
        CancellationToken cancellationToken)
    {
        var apiKey = GetApiKey(agent);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/responses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = JsonContent.Create(new
        {
            model = agent.Model,
            instructions = agent.SystemPrompt,
            input = BuildPrompt(agent, incomingMessage, prompt)
        }, options: JsonOptions);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"OpenAI returned {(int)response.StatusCode}: {json}");
        }

        return ExtractText(json);
    }

    private static string GetApiKey(AgentConfig agent)
    {
        var envVar = string.IsNullOrWhiteSpace(agent.ApiKeyEnvironmentVariable)
            ? "OPENAI_API_KEY"
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

        if (root.TryGetProperty("output_text", out var outputText)
            && outputText.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(outputText.GetString()))
        {
            return outputText.GetString()!.Trim();
        }

        if (root.TryGetProperty("output", out var output)
            && output.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in output.EnumerateArray())
            {
                if (!item.TryGetProperty("content", out var content)
                    || content.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var part in content.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var text)
                        && text.ValueKind == JsonValueKind.String
                        && !string.IsNullOrWhiteSpace(text.GetString()))
                    {
                        return text.GetString()!.Trim();
                    }
                }
            }
        }

        throw new InvalidOperationException("OpenAI response did not contain text output.");
    }
}
