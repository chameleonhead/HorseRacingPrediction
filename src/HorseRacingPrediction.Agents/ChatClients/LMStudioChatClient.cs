using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace HorseRacingPrediction.Agents.ChatClients;

/// <summary>
/// Implements <see cref="IChatClient"/> for LM Studio's REST chat API.
/// </summary>
public sealed class LMStudioChatClient : IChatClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _disposeHttpClient;
    private readonly JsonSerializerOptions _jsonSerializerOptions;
    private readonly LMStudioChatClientOptions _options;
    private readonly ChatClientMetadata _metadata;

    /// <summary>
    /// Initializes a new instance of <see cref="LMStudioChatClient"/>.
    /// </summary>
    /// <param name="options">The configuration used to contact LM Studio.</param>
    public LMStudioChatClient(LMStudioChatClientOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _jsonSerializerOptions = _options.JsonSerializerOptions ?? new(JsonSerializerDefaults.Web);

        if (_options.HttpClient is null)
        {
            _httpClient = new HttpClient
            {
                Timeout = Timeout.InfiniteTimeSpan,
            };
            _disposeHttpClient = true;
        }
        else
        {
            _httpClient = _options.HttpClient;
            _disposeHttpClient = false;
        }

        if (!string.IsNullOrWhiteSpace(_options.ApiToken))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiToken);
        }

        _metadata = new("lmstudio", _options.BaseUri, _options.DefaultModel);
    }

    /// <inheritdoc/>
    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        // Use OpenAI-compatible endpoint when tools are provided
        if (HasTools(options))
        {
            OpenAIChatCompletionsRequest openAIRequest = BuildOpenAIRequest(messages, options);
            JsonElement responseElement = await SendOpenAIChatRequestAsync(openAIRequest, cancellationToken).ConfigureAwait(false);
            ChatResponse response = _options.ResponseParser?.Invoke(responseElement) ?? ParseOpenAIResponse(responseElement);
            return response;
        }

        LMStudioChatRequest requestPayload = BuildRequest(messages, options);
        JsonElement responseElement2 = await SendChatRequestAsync(requestPayload, cancellationToken).ConfigureAwait(false);
        ChatResponse response2 = _options.ResponseParser?.Invoke(responseElement2) ?? ParseResponse(responseElement2);
        return response2;
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        return GetStreamingResponseAsyncCore(messages, options, cancellationToken);
    }

    /// <inheritdoc/>
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        return serviceKey is not null ? null :
            serviceType == typeof(ChatClientMetadata) ? _metadata :
            serviceType == typeof(LMStudioChatClientOptions) ? _options :
            serviceType.IsInstanceOfType(this) ? this :
            null;
    }

    private async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsyncCore(IEnumerable<ChatMessage> messages, ChatOptions? options, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ChatResponse response = await GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        foreach (ChatResponseUpdate update in response.ToChatResponseUpdates())
        {
            yield return update;
        }
    }

    private LMStudioChatRequest BuildRequest(IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        if (options?.RawRepresentationFactory?.Invoke(this) is LMStudioChatRequest customRequest)
        {
            return customRequest;
        }

        string model = options?.ModelId ?? _options.DefaultModel ?? throw new InvalidOperationException("LM Studio model must be specified.");
        var messageList = messages as IList<ChatMessage> ?? messages.ToList();
        (string? systemPrompt, IReadOnlyList<ChatMessage> payloadMessages) = SeparateSystemPrompt(messageList, options);

        double? temperature = options?.Temperature ?? _options.DefaultTemperature;
        int? contextLength = GetContextLength(options) ?? _options.ContextLength;
        bool? stream = GetStreamFlag(options) ?? _options.Stream;
        double? topP = options?.TopP ?? TryGetDouble(options?.AdditionalProperties, "top_p");
        int? topK = options?.TopK ?? TryGetInt(options?.AdditionalProperties, "top_k");
        double? minP = TryGetDouble(options?.AdditionalProperties, "min_p");
        double? repeatPenalty = TryGetDouble(options?.AdditionalProperties, "repeat_penalty");
        int? maxOutputTokens = options?.MaxOutputTokens ?? TryGetInt(options?.AdditionalProperties, "max_output_tokens");
        string? reasoning = GetReasoningOption(options);
        bool? store = TryGetBool(options?.AdditionalProperties, "store");
        string? previousResponseId = TryGetString(options?.AdditionalProperties, "previous_response_id");
        IReadOnlyList<object>? integrations = GetIntegrations(options);

        IReadOnlyList<LMStudioChatMessage> input = ConvertMessages(payloadMessages);

        return new LMStudioChatRequest
        {
            Model = model,
            Input = input,
            SystemPrompt = systemPrompt,
            Integrations = integrations,
            Temperature = temperature,
            ContextLength = contextLength,
            Stream = stream,
            TopP = topP,
            TopK = topK,
            MinP = minP,
            RepeatPenalty = repeatPenalty,
            MaxOutputTokens = maxOutputTokens,
            Reasoning = reasoning,
            Store = store,
            PreviousResponseId = previousResponseId,
        };
    }

    private IReadOnlyList<LMStudioChatMessage> ConvertMessages(IEnumerable<ChatMessage> messages)
    {
        var payload = new List<LMStudioChatMessage>();
        foreach (ChatMessage message in messages)
        {
            if (message.RawRepresentation is LMStudioChatMessage cached)
            {
                payload.Add(cached);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(message.Text))
            {
                payload.Add(CreateTextMessage(message.Text));
            }

            foreach (AIContent content in message.Contents)
            {
                LMStudioChatMessage? entry = ConvertAIContentToMessage(content);
                if (entry is not null)
                {
                    payload.Add(entry);
                }
            }
        }

        return payload;
    }

    private LMStudioChatMessage? ConvertAIContentToMessage(AIContent content)
    {
        switch (content)
        {
            case TextContent:
                return null;

            case TextReasoningContent reasoning when !string.IsNullOrWhiteSpace(reasoning.Text):
                return CreateTextMessage($"[Reasoning] {reasoning.Text}");

            case FunctionCallContent call:
                return CreateTextMessage(FormatFunctionCall(call));

            case FunctionResultContent result:
                return CreateTextMessage(FormatFunctionResult(result));

            case ErrorContent error:
                return CreateTextMessage(FormatError(error));

            case UriContent uri when uri.HasTopLevelMediaType("image"):
                return CreateImageMessage(uri.Uri.ToString());

            case DataContent data when data.HasTopLevelMediaType("image"):
                return CreateImageMessage(data.Uri);

            case UsageContent usage:
                {
                    string? usageText = SerializeObjectToText(usage.Details);
                    return usageText is null ? null : CreateTextMessage($"Usage: {usageText}");
                }
            default:
                {
                    string? fallback = SerializeObjectToText(content.RawRepresentation ?? content);
                    return string.IsNullOrWhiteSpace(fallback) ? null : CreateTextMessage(fallback);
                }
        }
    }

    private static LMStudioChatMessage CreateTextMessage(string text)
        => new()
        {
            Type = "text",
            Content = text,
        };

    private static LMStudioChatMessage CreateImageMessage(string dataUrl)
        => new()
        {
            Type = "image",
            DataUrl = dataUrl,
        };

    private static (string? SystemPrompt, IReadOnlyList<ChatMessage> Messages) SeparateSystemPrompt(IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        string? systemPrompt = options?.Instructions;
        var filtered = new List<ChatMessage>();

        foreach (ChatMessage message in messages)
        {
            if (message.Role == ChatRole.System)
            {
                if (string.IsNullOrWhiteSpace(systemPrompt))
                {
                    string candidate = message.Text;
                    if (!string.IsNullOrWhiteSpace(candidate))
                    {
                        systemPrompt = candidate;
                    }
                }

                continue;
            }

            filtered.Add(message);
        }

        return (systemPrompt, filtered);
    }

    private static string? GetReasoningOption(ChatOptions? options)
    {
        if (options?.Reasoning?.Effort is ReasoningEffort effort)
        {
            return effort switch
            {
                ReasoningEffort.None => "off",
                ReasoningEffort.Low => "low",
                ReasoningEffort.Medium => "medium",
                ReasoningEffort.High => "high",
                ReasoningEffort.ExtraHigh => "high",
                _ => null,
            };
        }

        return TryGetString(options?.AdditionalProperties, "reasoning");
    }

    private static IReadOnlyList<object>? GetIntegrations(ChatOptions? options)
    {
        if (options?.AdditionalProperties?.TryGetValue("integrations", out object? value) is not true || value is null)
        {
            return null;
        }

        if (value is string text)
        {
            return new object[] { text };
        }

        if (value is IReadOnlyList<object> existingList)
        {
            return existingList;
        }

        if (value is IEnumerable<object> enumerableObjects)
        {
            return enumerableObjects.ToList();
        }

        if (value is IEnumerable enumerable)
        {
            var converted = new List<object>();
            foreach (object? item in enumerable)
            {
                if (item is not null)
                {
                    converted.Add(item);
                }
            }

            if (converted.Count > 0)
            {
                return converted;
            }
        }

        return new object[] { value };
    }

    private string FormatFunctionCall(FunctionCallContent call)
    {
        string? args = SerializeObjectToText(call.Arguments);
        return args is null ? $"Function call: {call.Name}" : $"Function call: {call.Name}({args})";
    }

    private string FormatFunctionResult(FunctionResultContent result)
    {
        string details = SerializeObjectToText(result.Result) ?? "(null)";
        return $"Function result ({result.CallId}): {details}";
    }

    private string FormatError(ErrorContent error)
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(error.ErrorCode))
        {
            builder.Append(error.ErrorCode);
        }
        else
        {
            builder.Append("Error");
        }

        if (!string.IsNullOrWhiteSpace(error.Message))
        {
            builder.Append(':').Append(' ').Append(error.Message);
        }

        if (!string.IsNullOrWhiteSpace(error.Details))
        {
            builder.Append(" - ").Append(error.Details);
        }

        return builder.ToString();
    }

    private string? SerializeObjectToText(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is string textValue)
        {
            return string.IsNullOrWhiteSpace(textValue) ? null : textValue;
        }

        if (value is JsonElement jsonElement)
        {
            return jsonElement.ToString();
        }

        try
        {
            return JsonSerializer.Serialize(value, _jsonSerializerOptions);
        }
        catch
        {
            return value.ToString();
        }
    }

    private static int? GetContextLength(ChatOptions? options)
    {
        return TryGetInt(options?.AdditionalProperties, "context_length");
    }

    private static bool? GetStreamFlag(ChatOptions? options)
    {
        return TryGetBool(options?.AdditionalProperties, "stream");
    }

    private static int? TryGetInt(AdditionalPropertiesDictionary? properties, string key)
    {
        if (properties?.TryGetValue(key, out object? value) is true)
        {
            return TryConvertToInt(value);
        }

        return null;
    }

    private static bool? TryGetBool(AdditionalPropertiesDictionary? properties, string key)
    {
        if (properties?.TryGetValue(key, out object? value) is true)
        {
            return TryConvertToBool(value);
        }

        return null;
    }

    private static double? TryGetDouble(AdditionalPropertiesDictionary? properties, string key)
    {
        if (properties?.TryGetValue(key, out object? value) is true)
        {
            return TryConvertToDouble(value);
        }

        return null;
    }

    private static string? TryGetString(AdditionalPropertiesDictionary? properties, string key)
    {
        if (properties?.TryGetValue(key, out object? value) is true)
        {
            return value switch
            {
                string stringValue => string.IsNullOrWhiteSpace(stringValue) ? null : stringValue,
                JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString(),
                _ => null,
            };
        }

        return null;
    }

    private static double? TryConvertToDouble(object? value)
    {
        return value switch
        {
            float floatValue => floatValue,
            double doubleValue => doubleValue,
            decimal decimalValue => (double)decimalValue,
            JsonElement element when element.TryGetDouble(out double result) => result,
            string stringValue when double.TryParse(stringValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) => parsed,
            _ => null,
        };
    }

    private static int? TryConvertToInt(object? value)
    {
        return value switch
        {
            int intValue => intValue,
            long longValue when longValue >= int.MinValue && longValue <= int.MaxValue => (int)longValue,
            double doubleValue when doubleValue % 1 == 0 && doubleValue >= int.MinValue && doubleValue <= int.MaxValue => (int)doubleValue,
            _ => null,
        };
    }

    private static bool? TryConvertToBool(object? value)
        => value switch
        {
            bool boolValue => boolValue,
            string stringValue when bool.TryParse(stringValue, out bool parsed) => parsed,
            _ => null,
        };

    private async Task<JsonElement> SendChatRequestAsync(LMStudioChatRequest payload, CancellationToken cancellationToken)
    {
        string payloadJson = JsonSerializer.Serialize(payload, _jsonSerializerOptions);
        // Console.WriteLine("LM Studio request:\n" + payloadJson);

        var requestUri = new Uri(_options.BaseUri, _options.ChatPath);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(payloadJson, Encoding.UTF8, "application/json"),
        };

        _options.ConfigureRequest?.Invoke(httpRequest);
        HttpResponseMessage response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        _options.ConfigureResponse?.Invoke(response);

        string responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"LM Studio request failed with {(int)response.StatusCode} {response.ReasonPhrase}. Body: {responseText}");
        }

        // Console.WriteLine("LM Studio response:\n" + responseText);

        using var document = JsonDocument.Parse(responseText);
        return document.RootElement.Clone();
    }

    #region OpenAI-Compatible Endpoint Support

    private static bool HasTools(ChatOptions? options)
        => options?.Tools is { Count: > 0 };

    private OpenAIChatCompletionsRequest BuildOpenAIRequest(IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        string model = options?.ModelId ?? _options.DefaultModel ?? throw new InvalidOperationException("LM Studio model must be specified.");
        var messageList = messages as IList<ChatMessage> ?? messages.ToList();

        double? temperature = options?.Temperature ?? _options.DefaultTemperature;
        double? topP = options?.TopP ?? TryGetDouble(options?.AdditionalProperties, "top_p");
        int? maxTokens = options?.MaxOutputTokens ?? TryGetInt(options?.AdditionalProperties, "max_tokens");
        bool? stream = GetStreamFlag(options) ?? _options.Stream;

        IReadOnlyList<OpenAIChatMessage> openAIMessages = ConvertToOpenAIMessages(messageList, options);
        IReadOnlyList<OpenAITool>? tools = ConvertToOpenAITools(options?.Tools);
        object? toolChoice = GetToolChoice(options);

        return new OpenAIChatCompletionsRequest
        {
            Model = model,
            Messages = openAIMessages,
            Temperature = temperature,
            TopP = topP,
            MaxTokens = maxTokens,
            Tools = tools,
            ToolChoice = toolChoice,
            Stream = stream,
        };
    }

    private IReadOnlyList<OpenAIChatMessage> ConvertToOpenAIMessages(IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        var result = new List<OpenAIChatMessage>();

        // Add system message from instructions if present
        if (!string.IsNullOrWhiteSpace(options?.Instructions))
        {
            result.Add(new OpenAIChatMessage
            {
                Role = "system",
                Content = options.Instructions,
            });
        }

        foreach (ChatMessage message in messages)
        {
            string role = GetOpenAIRole(message.Role);

            // Handle tool result messages
            if (message.Role == ChatRole.Tool)
            {
                foreach (AIContent content in message.Contents)
                {
                    if (content is FunctionResultContent functionResult)
                    {
                        result.Add(new OpenAIChatMessage
                        {
                            Role = "tool",
                            Content = SerializeObjectToText(functionResult.Result) ?? string.Empty,
                            ToolCallId = functionResult.CallId,
                        });
                    }
                }
                continue;
            }

            // Handle assistant messages with tool calls
            var toolCalls = new List<OpenAIToolCall>();
            var textContents = new List<string>();

            foreach (AIContent content in message.Contents)
            {
                if (content is FunctionCallContent functionCall)
                {
                    toolCalls.Add(new OpenAIToolCall
                    {
                        Id = functionCall.CallId ?? Guid.NewGuid().ToString(),
                        Type = "function",
                        Function = new OpenAIFunctionCall
                        {
                            Name = functionCall.Name,
                            Arguments = SerializeObjectToText(functionCall.Arguments) ?? "{}",
                        },
                    });
                }
                else if (content is TextContent textContent && !string.IsNullOrWhiteSpace(textContent.Text))
                {
                    textContents.Add(textContent.Text);
                }
            }

            if (toolCalls.Count > 0)
            {
                result.Add(new OpenAIChatMessage
                {
                    Role = role,
                    Content = textContents.Count > 0 ? string.Join("\n", textContents) : null,
                    ToolCalls = toolCalls,
                });
            }
            else if (!string.IsNullOrWhiteSpace(message.Text))
            {
                result.Add(new OpenAIChatMessage
                {
                    Role = role,
                    Content = message.Text,
                });
            }
        }

        return result;
    }

    private static string GetOpenAIRole(ChatRole role)
    {
        if (role == ChatRole.System) return "system";
        if (role == ChatRole.User) return "user";
        if (role == ChatRole.Assistant) return "assistant";
        if (role == ChatRole.Tool) return "tool";
        return "user";
    }

    private static IReadOnlyList<OpenAITool>? ConvertToOpenAITools(IList<AITool>? tools)
    {
        if (tools is null || tools.Count == 0)
        {
            return null;
        }

        var result = new List<OpenAITool>(tools.Count);

        foreach (AITool tool in tools)
        {
            if (tool is AIFunction function)
            {
                result.Add(new OpenAITool
                {
                    Type = "function",
                    Function = new OpenAIFunctionDefinition
                    {
                        Name = function.Name,
                        Description = string.IsNullOrWhiteSpace(function.Description) ? null : function.Description,
                        Parameters = function.JsonSchema.ValueKind != JsonValueKind.Undefined ? function.JsonSchema : null,
                    },
                });
            }
        }

        return result.Count > 0 ? result : null;
    }

    private static object? GetToolChoice(ChatOptions? options)
    {
        if (options?.ToolMode is null)
        {
            return null;
        }

        if (options.ToolMode == ChatToolMode.Auto)
        {
            return "auto";
        }

        if (options.ToolMode == ChatToolMode.RequireAny)
        {
            return "required";
        }

        // Check for specific required tool
        if (options.ToolMode is RequiredChatToolMode required && !string.IsNullOrWhiteSpace(required.RequiredFunctionName))
        {
            return new { type = "function", function = new { name = required.RequiredFunctionName } };
        }

        return null;
    }

    private async Task<JsonElement> SendOpenAIChatRequestAsync(OpenAIChatCompletionsRequest payload, CancellationToken cancellationToken)
    {
        string payloadJson = JsonSerializer.Serialize(payload, _jsonSerializerOptions);
        // Console.WriteLine("LM Studio OpenAI-compat request:\n" + payloadJson);

        var requestUri = new Uri(_options.BaseUri, _options.OpenAIChatCompletionsPath);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(payloadJson, Encoding.UTF8, "application/json"),
        };

        _options.ConfigureRequest?.Invoke(httpRequest);
        HttpResponseMessage response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        _options.ConfigureResponse?.Invoke(response);

        string responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"LM Studio OpenAI request failed with {(int)response.StatusCode} {response.ReasonPhrase}. Body: {responseText}");
        }

        // Console.WriteLine("LM Studio OpenAI-compat response:\n" + responseText);

        using var document = JsonDocument.Parse(responseText);
        return document.RootElement.Clone();
    }

    private ChatResponse ParseOpenAIResponse(JsonElement response)
    {
        var messages = new List<ChatMessage>();

        if (response.TryGetProperty("choices", out JsonElement choices) && choices.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement choice in choices.EnumerateArray())
            {
                if (choice.TryGetProperty("message", out JsonElement messageElement))
                {
                    ChatMessage message = ParseOpenAIMessage(messageElement);
                    messages.Add(message);
                }
            }
        }

        if (messages.Count == 0)
        {
            messages.Add(new ChatMessage(ChatRole.Assistant, string.Empty));
        }

        var chatResponse = new ChatResponse(messages)
        {
            RawRepresentation = response.Clone(),
        };

        if (response.TryGetProperty("id", out JsonElement idElement) && idElement.ValueKind == JsonValueKind.String)
        {
            chatResponse.ResponseId = idElement.GetString();
        }

        if (response.TryGetProperty("model", out JsonElement modelElement) && modelElement.ValueKind == JsonValueKind.String)
        {
            chatResponse.ModelId = modelElement.GetString();
        }

        if (response.TryGetProperty("created", out JsonElement createdElement) && createdElement.TryGetInt64(out long createdUnix))
        {
            chatResponse.CreatedAt = DateTimeOffset.FromUnixTimeSeconds(createdUnix);
        }

        chatResponse.Usage = ParseOpenAIUsage(response);

        // Check finish reason for tool calls
        if (response.TryGetProperty("choices", out JsonElement choicesForFinish) &&
            choicesForFinish.ValueKind == JsonValueKind.Array &&
            choicesForFinish.GetArrayLength() > 0)
        {
            JsonElement firstChoice = choicesForFinish[0];
            if (firstChoice.TryGetProperty("finish_reason", out JsonElement finishReason) &&
                finishReason.ValueKind == JsonValueKind.String &&
                finishReason.GetString() == "tool_calls")
            {
                chatResponse.FinishReason = ChatFinishReason.ToolCalls;
            }
            else if (finishReason.ValueKind == JsonValueKind.String && finishReason.GetString() == "stop")
            {
                chatResponse.FinishReason = ChatFinishReason.Stop;
            }
        }

        return chatResponse;
    }

    private ChatMessage ParseOpenAIMessage(JsonElement messageElement)
    {
        string roleStr = "assistant";
        if (messageElement.TryGetProperty("role", out JsonElement roleElement) && roleElement.ValueKind == JsonValueKind.String)
        {
            roleStr = roleElement.GetString() ?? "assistant";
        }

        ChatRole role = ParseRole(roleStr);
        var contents = new List<AIContent>();

        // Parse text content
        if (messageElement.TryGetProperty("content", out JsonElement contentElement))
        {
            if (contentElement.ValueKind == JsonValueKind.String)
            {
                string? text = contentElement.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    contents.Add(new TextContent(text));
                }
            }
        }

        // Parse tool calls
        if (messageElement.TryGetProperty("tool_calls", out JsonElement toolCallsElement) &&
            toolCallsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement toolCall in toolCallsElement.EnumerateArray())
            {
                if (toolCall.TryGetProperty("type", out JsonElement typeElement) &&
                    typeElement.ValueKind == JsonValueKind.String &&
                    typeElement.GetString() == "function" &&
                    toolCall.TryGetProperty("function", out JsonElement functionElement))
                {
                    string? callId = null;
                    if (toolCall.TryGetProperty("id", out JsonElement idElement) && idElement.ValueKind == JsonValueKind.String)
                    {
                        callId = idElement.GetString();
                    }

                    string? functionName = null;
                    if (functionElement.TryGetProperty("name", out JsonElement nameElement) && nameElement.ValueKind == JsonValueKind.String)
                    {
                        functionName = nameElement.GetString();
                    }

                    IDictionary<string, object?>? arguments = null;
                    if (functionElement.TryGetProperty("arguments", out JsonElement argsElement) && argsElement.ValueKind == JsonValueKind.String)
                    {
                        string? argsJson = argsElement.GetString();
                        if (!string.IsNullOrWhiteSpace(argsJson))
                        {
                            try
                            {
                                arguments = JsonSerializer.Deserialize<Dictionary<string, object?>>(argsJson, _jsonSerializerOptions);
                            }
                            catch
                            {
                                // If parsing fails, wrap the raw string
                                arguments = new Dictionary<string, object?> { ["raw"] = argsJson };
                            }
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(functionName))
                    {
                        contents.Add(new FunctionCallContent(callId ?? string.Empty, functionName!, arguments));
                    }
                }
            }
        }

        return new ChatMessage(role, contents);
    }

    private static UsageDetails? ParseOpenAIUsage(JsonElement response)
    {
        if (!response.TryGetProperty("usage", out JsonElement usageElement))
        {
            return null;
        }

        var usage = new UsageDetails();

        if (usageElement.TryGetProperty("prompt_tokens", out JsonElement promptTokens) && promptTokens.TryGetInt64(out long promptCount))
        {
            usage.InputTokenCount = promptCount;
        }

        if (usageElement.TryGetProperty("completion_tokens", out JsonElement completionTokens) && completionTokens.TryGetInt64(out long completionCount))
        {
            usage.OutputTokenCount = completionCount;
        }

        if (usageElement.TryGetProperty("total_tokens", out JsonElement totalTokens) && totalTokens.TryGetInt64(out long totalCount))
        {
            usage.TotalTokenCount = totalCount;
        }

        return usage.InputTokenCount is null && usage.OutputTokenCount is null && usage.TotalTokenCount is null ? null : usage;
    }

    #endregion

    private static ChatResponse ParseResponse(JsonElement response)
    {
        List<ChatMessage> messages = ExtractMessages(response);
        var chatResponse = new ChatResponse(messages)
        {
            RawRepresentation = response.Clone(),
        };

        if (response.TryGetProperty("id", out JsonElement idElement) && idElement.ValueKind == JsonValueKind.String)
        {
            chatResponse.ResponseId = idElement.GetString();
        }

        if (response.TryGetProperty("session_id", out JsonElement sessionElement) && sessionElement.ValueKind == JsonValueKind.String)
        {
            chatResponse.ConversationId = sessionElement.GetString();
        }

        if (response.TryGetProperty("model", out JsonElement modelElement) && modelElement.ValueKind == JsonValueKind.String)
        {
            chatResponse.ModelId = modelElement.GetString();
        }

        if (response.TryGetProperty("created_at", out JsonElement createdAtElement) && createdAtElement.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(createdAtElement.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out DateTimeOffset created))
        {
            chatResponse.CreatedAt = created;
        }

        chatResponse.Usage = ParseUsage(response);

        return chatResponse;
    }

    private static UsageDetails? ParseUsage(JsonElement response)
    {
        if (!response.TryGetProperty("usage", out JsonElement usageElement))
        {
            return null;
        }

        var usage = new UsageDetails();
        SetToken(usageElement, "prompt_tokens", value => usage.InputTokenCount = value);
        SetToken(usageElement, "completion_tokens", value => usage.OutputTokenCount = value);
        SetToken(usageElement, "total_tokens", value => usage.TotalTokenCount = value);

        return usage.InputTokenCount is null && usage.OutputTokenCount is null && usage.TotalTokenCount is null ? null : usage;

        static void SetToken(JsonElement container, string name, Action<long> assign)
        {
            if (container.TryGetProperty(name, out JsonElement tokenElement) && tokenElement.TryGetInt64(out long tokenCount))
            {
                assign(tokenCount);
            }
        }
    }

    private static List<ChatMessage> ExtractMessages(JsonElement element)
    {
        var tuples = new List<(string Role, string Text)>();

        if (TryExtractMessagesFromChoices(element, tuples))
        {
            return ConvertTuples(tuples);
        }

        if (element.TryGetProperty("response", out JsonElement responseElement))
        {
            if (TryExtractMessagesFromChoices(responseElement, tuples))
            {
                return ConvertTuples(tuples);
            }
        }

        if (TryExtractFromArrayProperty(element, "messages", tuples) || TryExtractFromArrayProperty(element, "outputs", tuples))
        {
            return ConvertTuples(tuples);
        }

        string fallback = FlattenTextContent(element);
        tuples.Add(("assistant", fallback));
        return ConvertTuples(tuples);
    }

    private static List<ChatMessage> ConvertTuples(List<(string Role, string Text)> tuples)
    {
        var messages = new List<ChatMessage>(tuples.Count);
        foreach (var (role, content) in tuples)
        {
            messages.Add(new ChatMessage(ParseRole(role), content));
        }

        return messages;
    }

    private static bool TryExtractMessagesFromChoices(JsonElement element, List<(string Role, string Text)> target)
    {
        if (!element.TryGetProperty("choices", out JsonElement choices) || choices.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (JsonElement choice in choices.EnumerateArray())
        {
            if (choice.TryGetProperty("message", out JsonElement message))
            {
                string role = GetRoleFromElement(message);
                string text = FlattenTextContent(message);
                target.Add((role, text));
                continue;
            }

            string fallbackRole = GetRoleFromElement(choice);
            string fallbackText = FlattenTextContent(choice);
            target.Add((fallbackRole, fallbackText));
        }

        return target.Count > 0;
    }

    private static bool TryExtractFromArrayProperty(JsonElement element, string propertyName, List<(string Role, string Text)> target)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement array) || array.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (JsonElement item in array.EnumerateArray())
        {
            string role = GetRoleFromElement(item);
            string text = FlattenTextContent(item);
            target.Add((role, text));
        }

        return true;
    }

    private static string GetRoleFromElement(JsonElement element)
    {
        if (element.TryGetProperty("role", out JsonElement roleElement) && roleElement.ValueKind == JsonValueKind.String)
        {
            return roleElement.GetString()?.ToLowerInvariant() ?? "assistant";
        }

        return "assistant";
    }

    private static string FlattenTextContent(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.Array => string.Join(Environment.NewLine, element.EnumerateArray().Select(FlattenTextContent).Where(text => !string.IsNullOrEmpty(text))),
            JsonValueKind.Object => FlattenTextFromObject(element),
            _ => string.Empty,
        };
    }

    private static string FlattenTextFromObject(JsonElement element)
    {
        if (element.TryGetProperty("text", out JsonElement textValue))
        {
            string text = FlattenTextContent(textValue);
            if (!string.IsNullOrEmpty(text))
            {
                return text;
            }
        }

        if (element.TryGetProperty("content", out JsonElement contentValue))
        {
            string text = FlattenTextContent(contentValue);
            if (!string.IsNullOrEmpty(text))
            {
                return text;
            }
        }

        var builder = new StringBuilder();
        foreach (JsonProperty child in element.EnumerateObject())
        {
            string childText = FlattenTextContent(child.Value);
            if (string.IsNullOrEmpty(childText))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(childText);
        }

        return builder.ToString();
    }

    private static ChatRole ParseRole(string role)
        => role switch
        {
            "system" => ChatRole.System,
            "user" => ChatRole.User,
            "assistant" => ChatRole.Assistant,
            "function" => ChatRole.Tool,
            _ => ChatRole.Assistant,
        };

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposeHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}

/// <summary>
/// Configuration options used by <see cref="LMStudioChatClient"/>.
/// </summary>
public sealed class LMStudioChatClientOptions
{
    /// <summary>
    /// Gets or sets the base endpoint for LM Studio (for example https://127.0.0.1:8080).
    /// </summary>
    public Uri BaseUri { get; set; } = new("https://localhost");

    /// <summary>
    /// Gets or sets the path to the chat endpoint (defaults to /api/v1/chat).
    /// </summary>
    public string ChatPath { get; set; } = "/api/v1/chat";

    /// <summary>
    /// Gets or sets the path to the OpenAI-compatible chat completions endpoint (defaults to /v1/chat/completions).
    /// Used automatically when tools are provided in ChatOptions.
    /// </summary>
    public string OpenAIChatCompletionsPath { get; set; } = "/v1/chat/completions";

    /// <summary>
    /// Gets or sets the LM Studio model that should be used by default.
    /// </summary>
    public string? DefaultModel { get; set; }

    /// <summary>
    /// Gets or sets a fallback temperature when no <see cref="ChatOptions"/> override is supplied.
    /// </summary>
    public double? DefaultTemperature { get; set; }

    /// <summary>
    /// Gets or sets the default context length to pass to LM Studio.
    /// </summary>
    public int? ContextLength { get; set; }

    /// <summary>
    /// Gets or sets whether LM Studio should stream its response by default.
    /// </summary>
    public bool? Stream { get; set; }

    /// <summary>
    /// Gets or sets the optional API token used for authentication.
    /// </summary>
    public string? ApiToken { get; set; }

    /// <summary>
    /// Gets or sets a shared <see cref="HttpClient"/> instance to use for network calls.
    /// </summary>
    public HttpClient? HttpClient { get; set; }

    /// <summary>
    /// Gets or sets serialization settings used when sending or receiving payloads.
    /// </summary>
    public JsonSerializerOptions? JsonSerializerOptions { get; set; }

    /// <summary>
    /// Gets or sets a hook that runs before each HTTP request is sent.
    /// </summary>
    public Action<HttpRequestMessage>? ConfigureRequest { get; set; }

    /// <summary>
    /// Gets or sets a hook that runs after a response is received.
    /// </summary>
    public Action<HttpResponseMessage>? ConfigureResponse { get; set; }

    /// <summary>
    /// Gets or sets a custom parser used to transform LM Studio responses into <see cref="ChatResponse"/> instances.
    /// </summary>
    public Func<JsonElement, ChatResponse>? ResponseParser { get; set; }
}

internal sealed class LMStudioChatRequest
{
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("input")]
    public IReadOnlyList<LMStudioChatMessage>? Input { get; set; }

    [JsonPropertyName("system_prompt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SystemPrompt { get; set; }

    [JsonPropertyName("integrations")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<object>? Integrations { get; set; }

    [JsonPropertyName("temperature")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Temperature { get; set; }

    [JsonPropertyName("top_p")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? TopP { get; set; }

    [JsonPropertyName("top_k")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TopK { get; set; }

    [JsonPropertyName("min_p")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? MinP { get; set; }

    [JsonPropertyName("repeat_penalty")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? RepeatPenalty { get; set; }

    [JsonPropertyName("max_output_tokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxOutputTokens { get; set; }

    [JsonPropertyName("context_length")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ContextLength { get; set; }

    [JsonPropertyName("stream")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Stream { get; set; }

    [JsonPropertyName("reasoning")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reasoning { get; set; }

    [JsonPropertyName("store")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Store { get; set; }

    [JsonPropertyName("previous_response_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PreviousResponseId { get; set; }
}

internal sealed class LMStudioChatMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    [JsonPropertyName("content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Content { get; set; }

    [JsonPropertyName("data_url")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DataUrl { get; set; }
}

#region OpenAI-Compatible Request/Response Types

/// <summary>
/// OpenAI-compatible chat completions request for /v1/chat/completions endpoint.
/// Used when tools are provided in ChatOptions.
/// </summary>
internal sealed class OpenAIChatCompletionsRequest
{
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("messages")]
    public IReadOnlyList<OpenAIChatMessage>? Messages { get; set; }

    [JsonPropertyName("temperature")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Temperature { get; set; }

    [JsonPropertyName("top_p")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? TopP { get; set; }

    [JsonPropertyName("max_tokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxTokens { get; set; }

    [JsonPropertyName("tools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<OpenAITool>? Tools { get; set; }

    [JsonPropertyName("tool_choice")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? ToolChoice { get; set; }

    [JsonPropertyName("stream")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Stream { get; set; }
}

internal sealed class OpenAIChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "user";

    [JsonPropertyName("content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Content { get; set; }

    [JsonPropertyName("tool_calls")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<OpenAIToolCall>? ToolCalls { get; set; }

    [JsonPropertyName("tool_call_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolCallId { get; set; }
}

internal sealed class OpenAITool
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    [JsonPropertyName("function")]
    public OpenAIFunctionDefinition? Function { get; set; }
}

internal sealed class OpenAIFunctionDefinition
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    [JsonPropertyName("parameters")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Parameters { get; set; }
}

internal sealed class OpenAIToolCall
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    [JsonPropertyName("function")]
    public OpenAIFunctionCall? Function { get; set; }
}

internal sealed class OpenAIFunctionCall
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("arguments")]
    public string? Arguments { get; set; }
}

#endregion