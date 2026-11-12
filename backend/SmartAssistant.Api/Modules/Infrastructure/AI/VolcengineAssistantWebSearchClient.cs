#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SmartAssistant.Modules.Application;
using SmartAssistant.Modules.Contracts;
using SmartAssistant.Platform;

namespace SmartAssistant.Modules.Infrastructure.AI;

/// <summary>
/// 联网搜索返回的来源引用，供回答展示和证据关联使用。
/// </summary>
public sealed record AssistantWebSearchReference(string Title, string Url, string Excerpt);
/// <summary>
/// 联网搜索的结构化结果及来源引用。
/// </summary>
public sealed record AssistantWebSearchResult(string Answer, IReadOnlyList<AssistantWebSearchReference> References);

/// <summary>
/// 定义按运行范围和搜索词执行联网搜索的服务接口。
/// </summary>
public interface IAssistantWebSearchClient
{
    /// <summary>
    /// 按执行范围中的联网搜索配置发起查询，并返回搜索内容与来源引用。
    /// </summary>
    Task<AssistantWebSearchResult> SearchAsync(AssistantWebSearchSettings settings, string query, CancellationToken ct = default);
}

/// <summary>
/// 调用火山引擎搜索服务，并将返回内容转换为统一的搜索结果。
/// </summary>
public sealed class VolcengineAssistantWebSearchClient(IHttpClientFactory clients) : IAssistantWebSearchClient
{
    private const int MaxResponseBytes = 1_048_576;
    private const int MaxAnswerLength = 12_000;
    private const int MaxReferences = 8;
    private const int MaxReferenceTextLength = 1200;

    /// <summary>
    /// 按执行范围中的联网搜索配置发起查询，并返回搜索内容与来源引用。
    /// </summary>
    public async Task<AssistantWebSearchResult> SearchAsync(
        AssistantWebSearchSettings settings, string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length > 500)
            throw new BizResultException(BusinessCodes.BusinessException, "assistant_web_search_query_invalid");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
        using var request = new HttpRequestMessage(HttpMethod.Post, settings.Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        request.Headers.TryAddWithoutValidation("ServiceName", settings.ServiceName);
        request.Content = new StringContent(JsonSerializer.Serialize(new
        {
            bot_id = settings.BotId,
            messages = new[] { new { role = "user", content = query.Trim() } },
            stream = true
        }), Encoding.UTF8, "application/json");

        try
        {
            using var response = await clients.CreateClient("assistant-volc-agent")
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
                throw new BizResultException(BusinessCodes.BusinessException, "assistant_web_search_provider_failed");
            if (!string.Equals(response.Content.Headers.ContentType?.MediaType, "text/event-stream",
                    StringComparison.OrdinalIgnoreCase))
                throw new BizResultException(BusinessCodes.BusinessException, "assistant_web_search_provider_failed");
            if (response.Content.Headers.ContentLength is > MaxResponseBytes)
                throw new BizResultException(BusinessCodes.BusinessException, "assistant_web_search_response_too_large");

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            return await ReadSseAsync(stream, timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new BizResultException(BusinessCodes.BusinessException, "assistant_web_search_timeout");
        }
        catch (HttpRequestException)
        {
            throw new BizResultException(BusinessCodes.BusinessException, "assistant_web_search_provider_unavailable");
        }
    }

    private static async Task<AssistantWebSearchResult> ReadSseAsync(Stream stream, CancellationToken ct)
    {
        var answer = new StringBuilder();
        var references = new List<AssistantWebSearchReference>();
        var frame = new StringBuilder();
        var line = new StringBuilder();
        var decoder = Encoding.UTF8.GetDecoder();
        var bytes = new byte[4096];
        var chars = new char[Encoding.UTF8.GetMaxCharCount(bytes.Length)];
        var receivedBytes = 0;
        var terminated = false;

        while (!terminated)
        {
            var read = await stream.ReadAsync(bytes.AsMemory(), ct);
            if (read == 0) break;
            receivedBytes += read;
            if (receivedBytes > MaxResponseBytes)
                throw new BizResultException(BusinessCodes.BusinessException, "assistant_web_search_response_too_large");

            var charCount = decoder.GetChars(bytes.AsSpan(0, read), chars.AsSpan(), flush: false);
            for (var index = 0; index < charCount; index++)
            {
                var character = chars[index];
                if (character == '\r') continue;
                if (character == '\n')
                {
                    terminated = ProcessLine(line.ToString(), frame, answer, references);
                    line.Clear();
                    if (terminated) break;
                }
                else
                {
                    line.Append(character);
                }
            }
        }

        if (!terminated)
        {
            var tailCount = decoder.GetChars(ReadOnlySpan<byte>.Empty, chars.AsSpan(), flush: true);
            for (var index = 0; index < tailCount; index++)
                if (chars[index] != '\r') line.Append(chars[index]);
            if (line.Length > 0) terminated = ProcessLine(line.ToString(), frame, answer, references);
            if (!terminated && frame.Length > 0) terminated = ProcessFrame(frame.ToString(), answer, references);
        }

        if (!terminated || answer.Length == 0)
            throw new BizResultException(BusinessCodes.BusinessException, "assistant_web_search_response_invalid");
        return new(answer.ToString(), references.Take(MaxReferences).ToArray());
    }

    private static bool ProcessLine(
        string line,
        StringBuilder frame,
        StringBuilder answer,
        List<AssistantWebSearchReference> references)
    {
        if (line.Length == 0)
        {
            var terminated = ProcessFrame(frame.ToString(), answer, references);
            frame.Clear();
            return terminated;
        }
        if (line.StartsWith(':') || !line.StartsWith("data:", StringComparison.Ordinal)) return false;
        if (frame.Length > 0) frame.Append('\n');
        frame.Append(line.AsSpan(5).TrimStart());
        return false;
    }

    private static bool ProcessFrame(string data, StringBuilder answer, List<AssistantWebSearchReference> references)
    {
        var payload = data.Trim();
        if (payload.Length == 0) return false;
        if (payload == "[DONE]") return true;
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new BizResultException(BusinessCodes.BusinessException, "assistant_web_search_response_invalid");
            if (root.TryGetProperty("error", out _))
                throw new BizResultException(BusinessCodes.BusinessException, "assistant_web_search_provider_failed");
            AddReferences(root, "search_results", references);
            AddReferences(root, "references", references);
            if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array)
            {
                foreach (var choice in choices.EnumerateArray())
                {
                    if (choice.ValueKind != JsonValueKind.Object)
                        throw new BizResultException(BusinessCodes.BusinessException, "assistant_web_search_response_invalid");
                    if (choice.TryGetProperty("delta", out var delta))
                    {
                        if (delta.ValueKind != JsonValueKind.Object)
                            throw new BizResultException(BusinessCodes.BusinessException, "assistant_web_search_response_invalid");
                        if (delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                        {
                            var text = content.GetString();
                            if (!string.IsNullOrEmpty(text) && answer.Length < MaxAnswerLength)
                                answer.Append(text.AsSpan(0, Math.Min(text.Length, MaxAnswerLength - answer.Length)));
                        }
                    }
                    if (choice.TryGetProperty("finish_reason", out var finishReason)
                        && finishReason.ValueKind == JsonValueKind.String
                        && string.Equals(finishReason.GetString(), "stop", StringComparison.Ordinal))
                        return true;
                }
            }
            return false;
        }
        catch (JsonException)
        {
            throw new BizResultException(BusinessCodes.BusinessException, "assistant_web_search_response_invalid");
        }
    }

    private static void AddReferences(JsonElement root, string property, List<AssistantWebSearchReference> references)
    {
        if (references.Count >= MaxReferences || !root.TryGetProperty(property, out var values)
            || values.ValueKind != JsonValueKind.Array) return;
        foreach (var value in values.EnumerateArray())
        {
            if (references.Count >= MaxReferences || value.ValueKind != JsonValueKind.Object) break;
            var url = ReadString(value, "url", "link");
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("https" or "http")) continue;
            var title = ReadString(value, "title", "name", "site_name");
            var excerpt = ReadString(value, "content", "summary", "text", "description");
            if (title.Length > MaxReferenceTextLength) title = title[..MaxReferenceTextLength];
            if (excerpt.Length > MaxReferenceTextLength) excerpt = excerpt[..MaxReferenceTextLength];
            if (references.Any(item => string.Equals(item.Url, uri.ToString(), StringComparison.OrdinalIgnoreCase))) continue;
            references.Add(new(title, uri.ToString(), excerpt));
        }
    }

    private static string ReadString(JsonElement value, params string[] names)
    {
        foreach (var name in names)
            if (value.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.String)
                return item.GetString()?.Trim() ?? string.Empty;
        return string.Empty;
    }
}

/// <summary>
/// 将已授权的联网搜索工具请求转换为搜索服务调用。
/// </summary>
public sealed class AssistantVolcengineWebSearchToolHandler(
    AssistantWebSearchConfigurationService configuration,
    IAssistantWebSearchClient client) : IAssistantToolHandler
{
    public string ToolId => "assistant.web_search";
    public string ContractVersion => "v1";

    public async Task<JsonElement> ExecuteAsync(
        JsonElement input, AssistantScopeSnapshot scope, CancellationToken cancellationToken = default)
    {
        if (input.ValueKind != JsonValueKind.Object || !input.TryGetProperty("query", out var queryNode)
            || queryNode.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(queryNode.GetString())
            || queryNode.GetString()!.Trim().Length > 500)
            throw new BizResultException(BusinessCodes.BusinessException, "assistant_web_search_query_invalid");
        var (settings, _) = await configuration.ResolveForRunAsync(scope.WebSearchConfigRef, cancellationToken);
        var result = await client.SearchAsync(settings, queryNode.GetString()!.Trim(), cancellationToken);
        return JsonSerializer.SerializeToElement(new
        {
            type = "web_search_result",
            answer = result.Answer,
            references = result.References.Select(reference => new
            {
                title = reference.Title,
                url = reference.Url,
                excerpt = reference.Excerpt
            })
        });
    }
}
