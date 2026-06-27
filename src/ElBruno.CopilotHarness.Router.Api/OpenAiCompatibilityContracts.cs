using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ElBruno.CopilotHarness.Router.Core;

namespace ElBruno.CopilotHarness.Router.Api;

public sealed record OpenAiModelsResponse(
    [property: JsonPropertyName("object")] string Object,
    [property: JsonPropertyName("data")] IReadOnlyList<OpenAiModelResponse> Data);

public sealed record OpenAiModelResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("object")] string Object,
    [property: JsonPropertyName("created")] long Created,
    [property: JsonPropertyName("owned_by")] string OwnedBy,
    [property: JsonPropertyName("deployment")] string Deployment,
    [property: JsonPropertyName("apiVersion")] string ApiVersion);

public sealed record OpenAiResponsesResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("object")] string Object,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("created")] long Created,
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("output")] IReadOnlyList<OpenAiResponseMessage> Output,
    [property: JsonPropertyName("output_text")] string OutputText,
    [property: JsonPropertyName("usage")] OpenAiResponseUsage? Usage);

public sealed record OpenAiResponseMessage(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] IReadOnlyList<OpenAiResponseContent> Content);

public sealed record OpenAiResponseContent(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text")] string Text);

public sealed record OpenAiResponseUsage(
    [property: JsonPropertyName("input_tokens")] int? InputTokens,
    [property: JsonPropertyName("output_tokens")] int? OutputTokens,
    [property: JsonPropertyName("total_tokens")] int? TotalTokens);

public static class OpenAiCompatibilityMapper
{
    public static OpenAiModelsResponse CreateModelsResponse(RoutingOptions options)
    {
        var models = options.Profiles
            .Where(entry => entry.Value.Enabled)
            .Select(entry => new OpenAiModelResponse(
                entry.Key,
                "model",
                0,
                "elbruno-copilotharness",
                entry.Value.Deployment,
                string.IsNullOrWhiteSpace(entry.Value.ApiVersion)
                    ? FoundryOptions.DefaultApiVersion
                    : entry.Value.ApiVersion))
            .ToList();

        return new OpenAiModelsResponse("list", models);
    }

    public static bool TryBuildChatCompletionsRequest(
        JsonObject responsesRequest,
        out JsonObject chatCompletionsRequest,
        out string? errorMessage)
    {
        chatCompletionsRequest = [];
        errorMessage = null;

        if (!TryGetBooleanValue(responsesRequest["stream"], out var stream))
        {
            errorMessage = "The stream field must be a boolean when provided.";
            return false;
        }

        if (stream)
        {
            errorMessage = "Streaming is not yet supported on /v1/responses.";
            return false;
        }

        if (GetStringValue(responsesRequest["model"]) is { Length: > 0 } requestedModel)
        {
            chatCompletionsRequest["model"] = requestedModel;
        }

        if (responsesRequest["temperature"] is JsonValue temperature && temperature.TryGetValue<double>(out var parsedTemperature))
        {
            chatCompletionsRequest["temperature"] = parsedTemperature;
        }

        if (responsesRequest["top_p"] is JsonValue topP && topP.TryGetValue<double>(out var parsedTopP))
        {
            chatCompletionsRequest["top_p"] = parsedTopP;
        }

        if (responsesRequest["max_output_tokens"] is JsonValue maxOutputTokens &&
            maxOutputTokens.TryGetValue<int>(out var parsedMaxOutputTokens))
        {
            chatCompletionsRequest["max_tokens"] = parsedMaxOutputTokens;
        }

        if (responsesRequest["messages"] is not null && responsesRequest["messages"] is not JsonArray)
        {
            errorMessage = "The messages field must be an array when provided.";
            return false;
        }

        var inputNode = responsesRequest["input"];
        if (responsesRequest["messages"] is null &&
            inputNode is not null &&
            !(inputNode is JsonArray) &&
            !(inputNode is JsonValue value && value.TryGetValue<string>(out _)))
        {
            errorMessage = "The input field must be a string or an array.";
            return false;
        }

        var messages = BuildMessagesArray(responsesRequest);
        if (messages.Count == 0)
        {
            errorMessage = "The request must include a non-empty 'input' or 'messages' field.";
            return false;
        }

        chatCompletionsRequest["messages"] = messages;
        chatCompletionsRequest["stream"] = false;
        return true;
    }

    public static OpenAiResponsesResponse CreateResponsesResponse(
        string upstreamBody,
        RoutingDecision routingDecision)
    {
        var upstreamJson = TryParseObject(upstreamBody);
        var outputText = GetOutputText(upstreamJson, upstreamBody);
        var id = GetStringValue(upstreamJson?["id"]);
        var created = upstreamJson?["created"] is JsonValue createdValue && createdValue.TryGetValue<long>(out var parsedCreated)
            ? parsedCreated
            : DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var usage = ParseUsage(upstreamJson?["usage"] as JsonObject);

        var responseId = id is { Length: > 0 } upstreamId
            ? upstreamId.StartsWith("resp_", StringComparison.Ordinal) ? upstreamId : $"resp_{upstreamId}"
            : $"resp_{routingDecision.ProfileName}_{created}";

        var message = new OpenAiResponseMessage(
            $"msg_{responseId}",
            "message",
            "completed",
            "assistant",
            [new OpenAiResponseContent("output_text", outputText)]);

        return new OpenAiResponsesResponse(
            responseId,
            "response",
            "completed",
            created,
            routingDecision.Profile.Deployment,
            [message],
            outputText,
            usage);
    }

    private static JsonArray BuildMessagesArray(JsonObject responsesRequest)
    {
        var messages = new JsonArray();
        var instructions = GetStringValue(responsesRequest["instructions"]);
        if (!string.IsNullOrWhiteSpace(instructions))
        {
            messages.Add(new JsonObject
            {
                ["role"] = "system",
                ["content"] = instructions
            });
        }

        if (responsesRequest["messages"] is JsonArray existingMessages)
        {
            foreach (var message in existingMessages)
            {
                messages.Add(message?.DeepClone());
            }

            return messages;
        }

        var input = responsesRequest["input"];
        if (input is JsonValue singleInput && singleInput.TryGetValue<string>(out var singleInputText) && !string.IsNullOrWhiteSpace(singleInputText))
        {
            messages.Add(CreateUserMessage(singleInputText));
            return messages;
        }

        if (input is not JsonArray inputArray)
        {
            return messages;
        }

        if (inputArray.All(static item => item is JsonObject message && message["role"] is not null))
        {
            foreach (var message in inputArray)
            {
                messages.Add(message?.DeepClone());
            }

            return messages;
        }

        var textParts = inputArray
            .OfType<JsonObject>()
            .Select(item => GetStringValue(item["text"]) ?? GetStringValue(item["content"]))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToList();

        if (textParts.Count > 0)
        {
            messages.Add(CreateUserMessage(string.Join(Environment.NewLine, textParts)));
        }

        return messages;
    }

    private static JsonObject CreateUserMessage(string content) => new()
    {
        ["role"] = "user",
        ["content"] = content
    };

    private static OpenAiResponseUsage? ParseUsage(JsonObject? usage)
    {
        if (usage is null)
        {
            return null;
        }

        var inputTokens = TryGetInt32Value(usage["prompt_tokens"]) ?? TryGetInt32Value(usage["input_tokens"]);
        var outputTokens = TryGetInt32Value(usage["completion_tokens"]) ?? TryGetInt32Value(usage["output_tokens"]);
        var totalTokens = TryGetInt32Value(usage["total_tokens"]);

        return new OpenAiResponseUsage(inputTokens, outputTokens, totalTokens);
    }

    private static string GetOutputText(JsonObject? response, string fallback)
    {
        if (response is null)
        {
            return fallback;
        }

        if (response["choices"] is not JsonArray choices || choices.Count == 0 || choices[0] is not JsonObject firstChoice)
        {
            return fallback;
        }

        if (firstChoice["message"] is not JsonObject message)
        {
            return fallback;
        }

        if (GetStringValue(message["content"]) is { Length: > 0 } singleContent)
        {
            return singleContent;
        }

        if (message["content"] is not JsonArray multiPartContent)
        {
            return fallback;
        }

        var textParts = multiPartContent
            .OfType<JsonObject>()
            .Select(item => GetStringValue(item["text"]))
            .Where(static text => !string.IsNullOrWhiteSpace(text))
            .ToList();

        return textParts.Count > 0
            ? string.Join(Environment.NewLine, textParts)
            : fallback;
    }

    private static JsonObject? TryParseObject(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(payload) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? GetStringValue(JsonNode? value)
    {
        if (value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var stringValue))
        {
            return stringValue;
        }

        return null;
    }

    private static bool TryGetBooleanValue(JsonNode? value, out bool parsedValue)
    {
        parsedValue = false;
        if (value is null)
        {
            return true;
        }

        return value is JsonValue jsonValue && jsonValue.TryGetValue<bool>(out parsedValue);
    }

    private static int? TryGetInt32Value(JsonNode? value)
    {
        if (value is not JsonValue jsonValue)
        {
            return null;
        }

        if (jsonValue.TryGetValue<int>(out var intValue))
        {
            return intValue;
        }

        if (jsonValue.TryGetValue<long>(out var longValue) && longValue is >= int.MinValue and <= int.MaxValue)
        {
            return (int)longValue;
        }

        return null;
    }
}
