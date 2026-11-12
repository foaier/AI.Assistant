#nullable enable
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using AI.Assistant.Models.AI;
using AI.Assistant.Models.Runtime;
using AI.Assistant.Platform;
using AI.Assistant.Services.Contracts;

namespace AI.Assistant.Services.Infrastructure.AI;

/// <summary>
/// 根据冻结的供应商引用解析当前可用的模型访问配置和凭据。
/// </summary>
public interface IAssistantProviderCredentialResolver
{
    /// <summary>
    /// 校验供应商可用状态和模型用途后，解析冻结修订中的地址及凭据。
    /// </summary>
    ValueTask<OpenAiCompatibleProviderCredential> ResolveAsync(
        AssistantProviderAccess access,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 调用 OpenAI 兼容的聊天流式接口，并转换为统一的模型片段。
/// 凭据在服务端按引用解析，仅用于供应商请求，不进入公开运行事件。
/// </summary>
public sealed class OpenAiCompatibleChatModelProvider : IChatModelProvider
{
    /// <summary>
    /// 定义打开兼容聊天模型模型供应商使用的用量补全配置值。
    /// </summary>
    private static readonly TimeSpan UsageAfterCompletionGracePeriod = TimeSpan.FromMilliseconds(500);
    /// <summary>
    /// 保存用于打开兼容聊天模型模型供应商事件编解码的序列化器。
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// 保存复用的 HTTP 传输依赖，用于访问配置的模型或搜索服务。
    /// </summary>
    private readonly HttpClient _httpClient;
    /// <summary>
    /// 保存助手模型供应商凭据解析器依赖，供打开兼容聊天模型模型供应商执行相关操作。
    /// </summary>
    private readonly IAssistantProviderCredentialResolver _credentialResolver;
    /// <summary>
    /// 保存打开兼容模型供应商选项依赖，供打开兼容聊天模型模型供应商执行相关操作。
    /// </summary>
    private readonly OpenAiCompatibleProviderOptions _options;

    /// <summary>
    /// 接收并保存打开兼容聊天模型模型供应商执行所需的依赖，建立后续操作使用的运行状态。
    /// </summary>
    public OpenAiCompatibleChatModelProvider(
        HttpClient httpClient,
        IAssistantProviderCredentialResolver credentialResolver,
        OpenAiCompatibleProviderOptions options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _credentialResolver = credentialResolver ?? throw new ArgumentNullException(nameof(credentialResolver));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (_options.Timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "Provider timeout must be positive.");
        if (_options.MaxResponseBytes < 1024)
            throw new ArgumentOutOfRangeException(nameof(options), "Provider response limit is too small.");
    }

    /// <summary>
    /// 发送模型请求并按到达顺序返回文本、工具参数、用量或错误片段，响应调用方取消。
    /// </summary>
    public async IAsyncEnumerable<AssistantProviderChunk> StreamAsync(
        AssistantProviderRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var credential = await _credentialResolver.ResolveAsync(
            new AssistantProviderAccess(request.ProviderProfileRef, request.Model),
            cancellationToken);
        ValidateBaseUrl(credential.BaseUrl);
        if (string.IsNullOrWhiteSpace(credential.ApiKey))
            throw new BizResultException(BusinessCodes.BusinessException, "assistant_provider_credential_missing");

        using var timeoutCts = new CancellationTokenSource(credential.Timeout ?? _options.Timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCts.Token);
        var channel = Channel.CreateUnbounded<AssistantProviderChunk>(
            new UnboundedChannelOptions { SingleWriter = true, SingleReader = true });
        var producer = ProduceAsync(
            request,
            credential,
            channel.Writer,
            cancellationToken,
            timeoutCts.Token,
            linkedCts.Token);

        try
        {
            await foreach (var chunk in channel.Reader.ReadAllAsync(cancellationToken))
                yield return chunk;
        }
        finally
        {
            linkedCts.Cancel();
            await producer;
        }
    }

    /// <summary>
    /// 发送模型请求并把响应流解析为有序的内容、工具调用和用量片段。
    /// </summary>
    private async Task ProduceAsync(
        AssistantProviderRequest request,
        OpenAiCompatibleProviderCredential credential,
        ChannelWriter<AssistantProviderChunk> writer,
        CancellationToken callerCancellationToken,
        CancellationToken timeoutToken,
        CancellationToken requestCancellationToken)
    {
        try
        {
            using var httpRequest = BuildRequest(request, credential);
            using var response = await _httpClient.SendAsync(
                httpRequest,
                HttpCompletionOption.ResponseHeadersRead,
                requestCancellationToken);

            var providerRequestId = GetProviderRequestId(response);
            if (!response.IsSuccessStatusCode)
            {
                await writer.WriteAsync(
                    new AssistantProviderChunk(
                        AssistantProviderChunkKind.Error,
                        Error: await ReadHttpErrorAsync(
                            response,
                            providerRequestId,
                            requestCancellationToken),
                        ProviderRequestId: providerRequestId),
                    callerCancellationToken);
                return;
            }

            await ReadResponseAsync(
                response,
                providerRequestId,
                writer,
                requestCancellationToken);
        }
        catch (OperationCanceledException) when (callerCancellationToken.IsCancellationRequested)
        {
            /*
             * 调用方取消属于控制流程信号，由运行编排器统一记录取消状态。
             */
        }
        catch (OperationCanceledException) when (timeoutToken.IsCancellationRequested)
        {
            await TryWriteAsync(
                writer,
                ErrorChunk(
                    "provider_timeout",
                    "The provider request timed out.",
                    retryable: true,
                    providerRequestId: null));
        }
        catch (HttpRequestException)
        {
            await TryWriteAsync(
                writer,
                ErrorChunk(
                    "provider_transport_failure",
                    "The provider request could not be completed.",
                    retryable: true,
                    providerRequestId: null));
        }
        finally
        {
            writer.TryComplete();
        }
    }

    /// <summary>
    /// 读取模型供应商的 HTTP 响应流，解析增量内容并处理调用方取消。
    /// </summary>
    private async Task ReadResponseAsync(
        HttpResponseMessage response,
        string? providerRequestId,
        ChannelWriter<AssistantProviderChunk> writer,
        CancellationToken cancellationToken)
    {
        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(
            responseStream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true);
        var buffer = new StringBuilder();
        var completed = false;
        using var completionGraceCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var readCancellationToken = cancellationToken;

        while (true)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync(readCancellationToken);
            }
            catch (OperationCanceledException) when (
                completed
                && completionGraceCts.IsCancellationRequested
                && !cancellationToken.IsCancellationRequested)
            {
                /*
                 * finish_reason 表示模型已结束，仅保留短暂窗口接收可选的用量尾包。
                 */
                return;
            }

            if (line is null)
            {
                var result = FlushEvent(buffer, providerRequestId, completed);
                var alreadyCompleted = completed;
                foreach (var chunk in result.Chunks)
                {
                    if (!alreadyCompleted || chunk.Kind == AssistantProviderChunkKind.Usage)
                        await writer.WriteAsync(chunk, cancellationToken);
                }
                completed = result.Completed;
                if (result.Stop)
                    return;
                if (!alreadyCompleted && completed)
                {
                    completionGraceCts.CancelAfter(UsageAfterCompletionGracePeriod);
                    readCancellationToken = completionGraceCts.Token;
                }
                return;
            }

            if (line.Length == 0)
            {
                var result = FlushEvent(buffer, providerRequestId, completed);
                var alreadyCompleted = completed;
                foreach (var chunk in result.Chunks)
                {
                    if (!alreadyCompleted || chunk.Kind == AssistantProviderChunkKind.Usage)
                        await writer.WriteAsync(chunk, cancellationToken);
                }
                completed = result.Completed;
                if (result.Stop)
                    return;
                if (!alreadyCompleted && completed)
                {
                    completionGraceCts.CancelAfter(UsageAfterCompletionGracePeriod);
                    readCancellationToken = completionGraceCts.Token;
                }
                continue;
            }

            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                continue;

            var data = line[5..].TrimStart();
            if (data == "[DONE]")
            {
                if (!completed)
                {
                    completed = true;
                    await writer.WriteAsync(
                        new AssistantProviderChunk(
                            AssistantProviderChunkKind.Completed,
                            FinishReason: "stop",
                            ProviderRequestId: providerRequestId),
                        cancellationToken);
                }

                return;
            }

            if (buffer.Length + data.Length > _options.MaxResponseBytes)
            {
                await writer.WriteAsync(
                    ErrorChunk(
                        "provider_response_too_large",
                        "The provider response exceeded the configured limit.",
                        retryable: false,
                        providerRequestId),
                    cancellationToken);
                return;
            }

            if (buffer.Length > 0)
                buffer.Append('\n');
            buffer.Append(data);
        }
    }

    /// <summary>
    /// 根据已校验的模型请求和凭据构造供应商协议请求。
    /// </summary>
    private HttpRequestMessage BuildRequest(
        AssistantProviderRequest request,
        OpenAiCompatibleProviderCredential credential)
    {
        var endpoint = BuildEndpoint(credential.BaseUrl);
        var payload = new OpenAiChatCompletionRequest
        {
            Model = Require(request.Model, "Model"),
            Messages = request.Messages is null
                ? throw new BizResultException(BusinessCodes.BusinessException, "Messages required")
                : MapMessages(request.Messages, request.Instructions),
            Temperature = request.Temperature,
            TopP = request.TopP,
            MaxTokens = request.MaxTokens,
            Stream = true,
            StreamOptions = request.IncludeUsage
                ? new OpenAiStreamOptions { IncludeUsage = true }
                : null,
            Tools = request.Tools is null ? null : MapTools(request.Tools),
            ToolChoice = request.ToolChoice,
            ParallelToolCalls = request.ParallelToolCalls
        };

        var body = JsonSerializer.Serialize(payload, SerializerOptions);
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.ApiKey);
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (!string.IsNullOrWhiteSpace(credential.Organization))
            httpRequest.Headers.TryAddWithoutValidation("OpenAI-Organization", credential.Organization);
        return httpRequest;
    }

    /// <summary>
    /// 将数据映射为消息，并统一相关格式。
    /// </summary>
    private static IReadOnlyList<OpenAiMessage> MapMessages(IReadOnlyList<AssistantProviderMessage> messages, string? instructions)
    {
        var result = new List<OpenAiMessage>(messages.Count);
        if (!string.IsNullOrWhiteSpace(instructions))
            result.Add(new OpenAiMessage { Role = "system", Content = instructions });
        foreach (var message in messages)
        {
            if (!Enum.IsDefined(message.Role))
                throw new BizResultException(BusinessCodes.BusinessException, "assistant_provider_message_role_invalid");

            result.Add(new OpenAiMessage
            {
                Role = message.Role switch
                {
                    AssistantMessageRole.User => "user",
                    AssistantMessageRole.Assistant => "assistant",
                    AssistantMessageRole.Tool => "tool",
                    _ => throw new BizResultException(
                        BusinessCodes.BusinessException,
                        "assistant_provider_message_role_invalid")
                },
                Content = message.Content ?? string.Empty,
                Name = message.Name,
                ToolCallId = message.ToolCallId,
                ToolCalls = message.ToolCalls?.Select(call => new OpenAiToolCall
                {
                    Id = call.ToolCallId,
                    Type = "function",
                    Function = new OpenAiToolCallFunction
                    {
                        Name = call.Name,
                        Arguments = call.ArgumentsJson
                    }
                }).ToArray()
            });
        }

        return result;
    }

    /// <summary>
    /// 将数据映射为工具，并统一相关格式。
    /// </summary>
    private static IReadOnlyList<OpenAiTool> MapTools(IReadOnlyList<AssistantToolDefinition> tools)
    {
        var result = new List<OpenAiTool>(tools.Count);
        foreach (var tool in tools)
        {
            if (string.IsNullOrWhiteSpace(tool.Name))
                throw new BizResultException(BusinessCodes.BusinessException, "assistant_provider_tool_name_required");
            result.Add(new OpenAiTool
            {
                Type = "function",
                Function = new OpenAiFunction
                {
                    Name = tool.Name.Trim(),
                    Description = tool.Description,
                    Parameters = tool.InputSchema
                }
            });
        }

        return result;
    }

    /// <summary>
    /// 完成当前事件缓冲区的解析，并把有效增量写入输出通道。
    /// </summary>
    private OpenAiFlushResult FlushEvent(
        StringBuilder buffer,
        string? providerRequestId,
        bool completed)
    {
        if (buffer.Length == 0)
            return new OpenAiFlushResult(Array.Empty<AssistantProviderChunk>(), completed, false);

        var data = buffer.ToString();
        buffer.Clear();

        try
        {
            using var document = JsonDocument.Parse(data);
            return MapResponse(document.RootElement, providerRequestId, completed);
        }
        catch (JsonException)
        {
            return new OpenAiFlushResult(
                new[]
                {
                    ErrorChunk(
                        "provider_malformed_stream",
                        "The provider returned malformed streaming data.",
                        retryable: false,
                        providerRequestId)
                },
                true,
                true);
        }
    }

    /// <summary>
    /// 将数据映射为响应，并统一相关格式。
    /// </summary>
    private static OpenAiFlushResult MapResponse(
        JsonElement root,
        string? providerRequestId,
        bool completed)
    {
        var chunks = new List<AssistantProviderChunk>();
        if (root.TryGetProperty("id", out var idElement)
            && idElement.ValueKind == JsonValueKind.String)
        {
            providerRequestId ??= idElement.GetString();
        }

        if (root.TryGetProperty("usage", out var usageElement)
            && usageElement.ValueKind == JsonValueKind.Object)
        {
            chunks.Add(new AssistantProviderChunk(
                AssistantProviderChunkKind.Usage,
                Usage: new AssistantProviderUsage(
                    ReadInt(usageElement, "prompt_tokens"),
                    ReadInt(usageElement, "completion_tokens"),
                    ReadInt(usageElement, "total_tokens")),
                ProviderRequestId: providerRequestId));
        }

        if (!root.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array)
            return new OpenAiFlushResult(chunks, completed, false);

        foreach (var choice in choices.EnumerateArray())
        {
            var finishReason = choice.TryGetProperty("finish_reason", out var finishElement)
                && finishElement.ValueKind == JsonValueKind.String
                ? finishElement.GetString()
                : null;

            if (choice.TryGetProperty("delta", out var delta)
                && delta.ValueKind == JsonValueKind.Object)
            {
                if (delta.TryGetProperty("content", out var content)
                    && content.ValueKind == JsonValueKind.String
                    && !string.IsNullOrEmpty(content.GetString()))
                {
                    chunks.Add(new AssistantProviderChunk(
                        AssistantProviderChunkKind.TextDelta,
                        TextDelta: content.GetString(),
                        ProviderRequestId: providerRequestId));
                }

                if (delta.TryGetProperty("tool_calls", out var toolCalls)
                    && toolCalls.ValueKind == JsonValueKind.Array)
                {
                    foreach (var toolCall in toolCalls.EnumerateArray())
                    {
                        var index = toolCall.TryGetProperty("index", out var indexElement)
                            && indexElement.TryGetInt32(out var parsedIndex)
                            ? parsedIndex
                            : 0;
                        var toolCallId = ReadString(toolCall, "id");
                        string? name = null;
                        string? arguments = null;
                        if (toolCall.TryGetProperty("function", out var function)
                            && function.ValueKind == JsonValueKind.Object)
                        {
                            name = ReadString(function, "name");
                            arguments = ReadString(function, "arguments");
                        }

                        chunks.Add(new AssistantProviderChunk(
                            AssistantProviderChunkKind.ToolCallDelta,
                            ToolCall: new AssistantProviderToolCallDelta(
                                index,
                                toolCallId,
                                name,
                                arguments),
                            ProviderRequestId: providerRequestId));
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(finishReason) && !completed)
            {
                completed = true;
                chunks.Add(new AssistantProviderChunk(
                    AssistantProviderChunkKind.Completed,
                    FinishReason: finishReason,
                    ProviderRequestId: providerRequestId));
            }
        }

        return new OpenAiFlushResult(chunks, completed, false);
    }

    /// <summary>
    /// 读取并规范化供应商错误响应，避免向调用方泄漏凭据或内部内容。
    /// </summary>
    private static async Task<AssistantProviderError> ReadHttpErrorAsync(
        HttpResponseMessage response,
        string? providerRequestId,
        CancellationToken cancellationToken)
    {
        var statusCode = (int)response.StatusCode;
        string? providerCode = null;
        var message = "The provider returned an error.";
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.Object)
            {
                providerCode = ReadString(error, "code");
                message = ReadString(error, "message") ?? message;
            }
        }
        catch (JsonException)
        {
            /*
             * 上游错误正文不是 JSON 时仍返回稳定的标准错误，不透传原始响应。
             */
        }

        return new AssistantProviderError(
            $"provider_http_{statusCode}",
            message,
            statusCode == 408 || statusCode == 409 || statusCode == 429 || statusCode >= 500,
            statusCode,
            providerRequestId ?? providerCode);
    }

    /// <summary>
    /// 尝试写入，并响应调用方的取消请求。
    /// </summary>
    private static async Task TryWriteAsync(
        ChannelWriter<AssistantProviderChunk> writer,
        AssistantProviderChunk chunk)
    {
        try
        {
            await writer.WriteAsync(chunk);
        }
        catch (ChannelClosedException)
        {
            /*
             * 规范化供应商终态错误期间下游已断开，无需继续写入传输通道。
             */
        }
    }

    /// <summary>
    /// 创建标准错误片段，供模型流以统一格式报告失败。
    /// </summary>
    private static AssistantProviderChunk ErrorChunk(
        string code,
        string message,
        bool retryable,
        string? providerRequestId)
        => new(
            AssistantProviderChunkKind.Error,
            Error: new AssistantProviderError(code, message, retryable, null, providerRequestId),
            ProviderRequestId: providerRequestId);

    /// <summary>
    /// 从供应商响应标头中提取请求标识，供日志和故障追踪使用。
    /// </summary>
    private static string? GetProviderRequestId(HttpResponseMessage response)
        => response.Headers.TryGetValues("x-request-id", out var values)
            ? values.FirstOrDefault()
            : null;

    /// <summary>
    /// 读取 JSON 字段中的字符串，并统一处理缺失值和空白内容。
    /// </summary>
    private static string? ReadString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    /// <summary>
    /// 读取 JSON 字段中的整数，并校验其数值格式。
    /// </summary>
    private static int? ReadInt(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property)
            && property.TryGetInt32(out var value)
            ? value
            : null;

    /// <summary>
    /// 规范化供应商接口地址并补齐所需的 API 路径。
    /// </summary>
    private static Uri BuildEndpoint(Uri baseUrl)
    {
        var baseText = baseUrl.ToString().TrimEnd('/');
        return new Uri(baseText.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
            ? $"{baseText}/chat/completions" : $"{baseText}/v1/chat/completions", UriKind.Absolute);
    }

    /// <summary>
    /// 校验供应商基础地址为受支持的安全 URI。
    /// </summary>
    private static void ValidateBaseUrl(Uri baseUrl)
    {
        if (baseUrl is null || !baseUrl.IsAbsoluteUri
            || !string.Equals(baseUrl.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new BizResultException(
                BusinessCodes.BusinessException,
                "assistant_provider_base_url_must_be_https");
        }
    }

    /// <summary>
    /// 校验必填文本并返回去除首尾空白后的值；内容缺失时报告输入错误。
    /// </summary>
    private static string Require(string? value, string field)
        => string.IsNullOrWhiteSpace(value)
            ? throw new BizResultException(BusinessCodes.BusinessException, $"{field} required")
            : value.Trim();
}
