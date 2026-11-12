// Spec: specs/136-public-assistant/requests/REQ-20260925-004-public-assistant-s2-chat-runtime-admission.md,
// S2-C provider streaming, tool-call assembly and normalized error contract.
#nullable enable
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SmartAssistant.Modules.Infrastructure.AI;
using SmartAssistant.Modules.Contracts;
using Xunit;

namespace SmartAssistant.Tests;

public sealed class OpenAiCompatibleChatModelProviderTests
{
    [Theory]
    [InlineData("https://provider.example", "https://provider.example/v1/chat/completions")]
    [InlineData("https://provider.example/v1/", "https://provider.example/v1/chat/completions")]
    [InlineData("https://dashscope.aliyuncs.com/compatible-mode/v1", "https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions")]
    public async Task Configured_api_root_is_preserved_without_duplicate_version(string root, string expected)
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "data: [DONE]\n\n");
        var provider = new OpenAiCompatibleChatModelProvider(new HttpClient(handler),
            new FakeCredentialResolver(root), new OpenAiCompatibleProviderOptions(TimeSpan.FromSeconds(5)));
        await ReadAllAsync(provider.StreamAsync(new AssistantProviderRequest("profile", "model", Array.Empty<AssistantProviderMessage>())));
        Assert.Equal(expected, handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task Streaming_response_normalizes_text_tool_call_usage_and_completion()
    {
        var handler = new RecordingHandler(
            HttpStatusCode.OK,
            """
            data: {"id":"req-1","choices":[{"delta":{"content":"hello"},"finish_reason":null}]}

            data: {"id":"req-1","choices":[{"delta":{"tool_calls":[{"index":0,"id":"call-1","function":{"name":"public.event.overview.read","arguments":"{\"event"}}]},"finish_reason":null}]}

            data: {"id":"req-1","choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"Id\":\"event-1\"}"}}]},"finish_reason":null}]}

            data: {"id":"req-1","choices":[],"usage":{"prompt_tokens":11,"completion_tokens":7,"total_tokens":18}}

            data: {"id":"req-1","choices":[{"delta":{},"finish_reason":"stop"}]}

            data: [DONE]

            """);
        var provider = CreateProvider(handler);

        var chunks = await ReadAllAsync(provider.StreamAsync(
            new AssistantProviderRequest(
                "profile-1",
                "model-1",
                new[]
                {
                    new AssistantProviderMessage(AssistantMessageRole.User, "What is next?")
                },
                Tools: new[]
                {
                    new AssistantToolDefinition(
                        "event-tool",
                        "v1",
                        "public.event.overview.read",
                        "Read public event overview.",
                        JsonDocument.Parse("""{"type":"object"}""").RootElement.Clone())
                })));

        Assert.Contains(chunks, chunk =>
            chunk.Kind == AssistantProviderChunkKind.TextDelta
            && chunk.TextDelta == "hello");
        Assert.Equal(
            2,
            chunks.FindAll(chunk => chunk.Kind == AssistantProviderChunkKind.ToolCallDelta).Count);
        Assert.Contains(chunks, chunk =>
            chunk.ToolCall?.ToolCallId == "call-1"
            && chunk.ToolCall?.Name == "public.event.overview.read");
        Assert.Contains(chunks, chunk =>
            chunk.ToolCall?.ArgumentsDelta == "Id\":\"event-1\"}");
        Assert.Contains(chunks, chunk =>
            chunk.Kind == AssistantProviderChunkKind.Usage
            && chunk.Usage?.TotalTokens == 18);
        Assert.Single(chunks.FindAll(chunk => chunk.Kind == AssistantProviderChunkKind.Completed));
        Assert.Equal("https://provider.example/v1/chat/completions", handler.LastRequestUri?.ToString());
        Assert.Equal("Bearer provider-secret", handler.LastAuthorization);

        using var requestDocument = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.True(requestDocument.RootElement.GetProperty("stream").GetBoolean());
        Assert.True(requestDocument.RootElement.GetProperty("stream_options").GetProperty("include_usage").GetBoolean());
        Assert.Equal(
            "public.event.overview.read",
            requestDocument.RootElement
                .GetProperty("tools")[0]
                .GetProperty("function")
                .GetProperty("name")
                .GetString());
    }

    [Fact]
    public async Task Http_error_is_normalized_without_returning_provider_secret()
    {
        var handler = new RecordingHandler(
            HttpStatusCode.TooManyRequests,
            """{"error":{"code":"rate_limit","message":"slow down"}}""");
        var provider = CreateProvider(handler);

        var chunks = await ReadAllAsync(provider.StreamAsync(
            new AssistantProviderRequest(
                "profile-1",
                "model-1",
                new[] { new AssistantProviderMessage(AssistantMessageRole.User, "hello") })));

        var error = Assert.Single(chunks);
        Assert.Equal(AssistantProviderChunkKind.Error, error.Kind);
        Assert.Equal("provider_http_429", error.Error?.Code);
        Assert.True(error.Error?.Retryable);
        Assert.DoesNotContain("provider-secret", error.Error?.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Malformed_sse_payload_is_normalized_and_stream_stops()
    {
        var handler = new RecordingHandler(
            HttpStatusCode.OK,
            """
            data: {not-json}

            data: {"choices":[{"delta":{"content":"must-not-leak"}}]}

            """);
        var provider = CreateProvider(handler);

        var chunks = await ReadAllAsync(provider.StreamAsync(
            new AssistantProviderRequest(
                "profile-1",
                "model-1",
                new[] { new AssistantProviderMessage(AssistantMessageRole.User, "hello") })));

        var error = Assert.Single(chunks);
        Assert.Equal(AssistantProviderChunkKind.Error, error.Kind);
        Assert.Equal("provider_malformed_stream", error.Error?.Code);
    }

    [Fact]
    public async Task Finish_reason_ends_an_open_stream_after_the_optional_usage_window()
    {
        var handler = new OpenEndedStreamHandler(
            "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}\r\n\r\n");
        var provider = CreateProvider(handler);
        using var cancellation = new CancellationTokenSource();
        var readTask = ReadAllAsync(provider.StreamAsync(
            new AssistantProviderRequest("profile-1", "model-1", Array.Empty<AssistantProviderMessage>()),
            cancellation.Token));

        List<AssistantProviderChunk> chunks;
        try
        {
            chunks = await readTask.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            cancellation.Cancel();
            if (!readTask.IsCompleted)
                await readTask;
        }

        Assert.Single(chunks.FindAll(chunk => chunk.Kind == AssistantProviderChunkKind.Completed));
        Assert.DoesNotContain(chunks, chunk => chunk.Kind == AssistantProviderChunkKind.Error);
    }

    [Fact]
    public async Task Non_https_provider_url_is_rejected_before_network_call()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "data: [DONE]\n\n");
        var provider = new OpenAiCompatibleChatModelProvider(
                new HttpClient(handler),
                new FakeCredentialResolver("http://provider.example"),
                new OpenAiCompatibleProviderOptions(TimeSpan.FromSeconds(5)));
        var exception = await Assert.ThrowsAsync<SmartAssistant.Platform.BizResultException>(() =>
            ReadAllAsync(provider.StreamAsync(new AssistantProviderRequest("profile", "model", Array.Empty<AssistantProviderMessage>()))));

        Assert.Equal("assistant_provider_base_url_must_be_https", exception.BusinessMessage);
        Assert.False(handler.WasCalled);
    }

    private static OpenAiCompatibleChatModelProvider CreateProvider(HttpMessageHandler handler)
        => new(
            new HttpClient(handler),
            new FakeCredentialResolver(),
            new OpenAiCompatibleProviderOptions(
                TimeSpan.FromSeconds(5)));

    private static async Task<List<AssistantProviderChunk>> ReadAllAsync(
        IAsyncEnumerable<AssistantProviderChunk> chunks)
    {
        var result = new List<AssistantProviderChunk>();
        await foreach (var chunk in chunks)
            result.Add(chunk);
        return result;
    }

    private sealed class FakeCredentialResolver : IAssistantProviderCredentialResolver
    {
        private readonly string _url;
        public FakeCredentialResolver(string url = "https://provider.example") => _url = url;
        public ValueTask<OpenAiCompatibleProviderCredential> ResolveAsync(
            AssistantProviderAccess access,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new OpenAiCompatibleProviderCredential(
                new Uri(_url),
                "provider-secret"));
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _body;

        public RecordingHandler(HttpStatusCode statusCode, string body)
        {
            _statusCode = statusCode;
            _body = body;
        }

        public bool WasCalled { get; private set; }
        public Uri? LastRequestUri { get; private set; }
        public string? LastAuthorization { get; private set; }
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            WasCalled = true;
            LastRequestUri = request.RequestUri;
            LastAuthorization = request.Headers.Authorization?.ToString();
            LastRequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(
                    _body,
                    Encoding.UTF8,
                    _statusCode == HttpStatusCode.OK ? "text/event-stream" : "application/json")
            };
        }
    }

    private sealed class OpenEndedStreamHandler : HttpMessageHandler
    {
        private readonly string _prefix;

        public OpenEndedStreamHandler(string prefix) => _prefix = prefix;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new OpenEndedSseContent(_prefix)
            });
    }

    private sealed class OpenEndedSseContent : HttpContent
    {
        private readonly byte[] _prefix;

        public OpenEndedSseContent(string prefix)
        {
            _prefix = Encoding.UTF8.GetBytes(prefix);
            Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
        }

        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
            => stream.WriteAsync(_prefix, 0, _prefix.Length);

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken)
            => Task.FromResult<Stream>(new PrefixThenOpenStream(_prefix));
    }

    private sealed class PrefixThenOpenStream : Stream
    {
        private readonly byte[] _prefix;
        private int _position;

        public PrefixThenOpenStream(byte[] prefix) => _prefix = prefix;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_position < _prefix.Length)
            {
                var count = Math.Min(buffer.Length, _prefix.Length - _position);
                _prefix.AsMemory(_position, count).CopyTo(buffer);
                _position += count;
                return count;
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }
}
