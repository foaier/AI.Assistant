using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using AI.Assistant.Models.Knowledge;
using Xunit;

namespace AI.Assistant.Tests;

// Exercise the real host, controllers, SQLite migrations and repositories. All
// files belong to a unique temporary test directory; the donor tree is unused.
/// <summary>
/// 验证独立项目行为和边界条件的自动化测试集合。
/// </summary>
public sealed class StandaloneHostTests
{
    /// <summary>
    /// 验证管理操作和文件上传无需登录或身份 Cookie。
    /// </summary>
    [Fact]
    public async Task Management_and_uploads_work_without_login_or_authentication_cookies()
    {
        var directory = TestHost.NewDirectory();
        using var host = new TestHost(directory);
        using var client = host.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = false });
        using var listed = await client.GetAsync("/api/admin/assistants");
        Assert.Empty((await Data(listed)).EnumerateArray());
        Assert.False(listed.Headers.Contains("Set-Cookie"));

        var assistant = await Data(await client.PostAsJsonAsync("/api/admin/assistants", new { name = "无需登录助手", configuration = new { } }));
        Assert.False(assistant.TryGetProperty("updatedBy", out _));
        Assert.False(assistant.TryGetProperty("updatedAt", out _));
        var id = assistant.GetProperty("id").GetString();
        Assert.Equal(id, (await Data(await client.GetAsync($"/api/admin/assistants/{id}"))).GetProperty("id").GetString());

        using var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent("assistant-image"), "category");
        var content = new ByteArrayContent(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jGxkAAAAASUVORK5CYII="));
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        multipart.Add(content, "file", "icon.png");
        using var uploaded = await client.PostAsync("/api/assets/upload", multipart);
        var asset = await Data(uploaded);
        Assert.Equal("image/png", asset.GetProperty("contentType").GetString());
        Assert.False(uploaded.Headers.Contains("Set-Cookie"));
        await Data(await client.DeleteAsync($"/api/admin/assistants/{id}"));
        Assert.Empty((await Data(await client.GetAsync("/api/admin/assistants"))).EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/auth/login", new { password = "unused" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.False(File.Exists(Path.Combine(directory, "admin-password.txt")));
    }

    /// <summary>
    /// 验证草稿的幂等、修订和物理删除，并确认重启后数据仍存在。
    /// </summary>
    [Fact]
    public async Task Drafts_preserve_idempotency_revisions_and_restart_persistence_with_physical_deletion()
    {
        var directory = TestHost.NewDirectory();
        string id;
        using (var host = new TestHost(directory))
        using (var client = host.CreateClient())
        {
            client.DefaultRequestHeaders.Add("Idempotency-Key", "standalone-create-once");
            var payload = new { name = "独立助手", description = "持久化证明", configuration = new { welcomeMessage = "欢迎" } };
            var created = await Data(await client.PostAsJsonAsync("/api/admin/assistants", payload));
            id = created.GetProperty("id").GetString()!;
            var replay = await Data(await client.PostAsJsonAsync("/api/admin/assistants", payload));
            Assert.Equal(id, replay.GetProperty("id").GetString());
            Assert.Single((await Data(await client.GetAsync("/api/admin/assistants"))).EnumerateArray());
            client.DefaultRequestHeaders.Remove("Idempotency-Key");

            var update = new { name = "已保存助手", description = "持久化证明", expectedDraftRevision = 1, configuration = new { welcomeMessage = "保存后欢迎" } };
            var saved = await Data(await client.PutAsJsonAsync($"/api/admin/assistants/{id}/draft", update));
            Assert.Equal(2, saved.GetProperty("draftRevision").GetInt64());
            var stale = await client.PutAsJsonAsync($"/api/admin/assistants/{id}/draft", update);
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            var readback = await Data(await client.GetAsync($"/api/admin/assistants/{id}"));
            Assert.Equal("已保存助手", readback.GetProperty("name").GetString());
            Assert.Equal("保存后欢迎", readback.GetProperty("configuration").GetProperty("welcomeMessage").GetString());
        }

        using (var restarted = new TestHost(directory))
        using (var client = restarted.CreateClient())
        {
            var readback = await Data(await client.GetAsync($"/api/admin/assistants/{id}"));
            Assert.Equal(2, readback.GetProperty("draftRevision").GetInt64());
            await Data(await client.DeleteAsync($"/api/admin/assistants/{id}"));
            Assert.Empty((await Data(await client.GetAsync("/api/admin/assistants"))).EnumerateArray());
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/admin/assistants/{id}")).StatusCode);
        }
    }

    /// <summary>
    /// 验证发布冻结公开配置，撤回后公开访问失效。
    /// </summary>
    [Fact]
    public async Task Publishing_freezes_the_public_configuration_and_revoking_removes_access()
    {
        using var host = new TestHost();
        using var client = host.CreateClient();
        var provider = await Data(await client.PostAsJsonAsync("/api/admin/system/assistant-models/providers", new
        {
            expectedRevision = 0,
            settings = new { name = "离线协议模型", baseUrl = "https://models.example.test/v1", secretReference = "test-token-material", secretVersion = "v1", models = new[] { new { id = "chat-test", purpose = "chat", supportsTools = false } } }
        }));
        var policy = await Data(await client.PostAsJsonAsync("/api/admin/assistant-models/policies", new
        {
            expectedRevision = 0,
            settings = new { name = "独立客服", businessCode = "standalone_service", providerId = provider.GetProperty("id").GetString(), providerRevision = provider.GetProperty("revision").GetInt64(), model = "chat-test", instructions = "使用中文回答。", knowledgeEnabled = false }
        }));
        var configuration = new { businessPolicyId = policy.GetProperty("id").GetString(), welcomeMessage = "发布时欢迎", anonymousChatEnabled = true };
        var assistant = await Data(await client.PostAsJsonAsync("/api/admin/assistants", new { name = "公开助手", configuration }));
        var id = assistant.GetProperty("id").GetString();
        var preview = await Data(await client.PostAsJsonAsync($"/api/admin/assistants/{id}/previews", new { expectedDraftRevision = 1 }));
        var previewId = preview.GetProperty("id").GetString();
        Assert.True(preview.GetProperty("expiresAt").GetDateTime() > DateTime.Now);
        var previewMessages = await Data(await client.GetAsync($"/api/admin/assistants/{id}/previews/{previewId}/messages"));
        Assert.Empty(previewMessages.GetProperty("items").EnumerateArray());
        var otherAssistant = await Data(await client.PostAsJsonAsync("/api/admin/assistants", new { name = "其他助手", configuration }));
        var otherId = otherAssistant.GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/admin/assistants/{otherId}/previews/{previewId}/messages")).StatusCode);
        var publication = await Data(await client.PostAsJsonAsync($"/api/admin/assistants/{id}/publish", new { expectedDraftRevision = 1, expectedPointerRevision = 0 }));
        Assert.Equal(1, publication.GetProperty("pointerRevision").GetInt64());
        var version = publication.GetProperty("version");
        Assert.False(version.TryGetProperty("publishedBy", out _));
        Assert.False(version.TryGetProperty("revokedBy", out _));
        var publishedAt = version.GetProperty("publishedAt").GetDateTime();
        Assert.Equal(DateTimeKind.Local, publishedAt.Kind);
        var publicationState = await Data(await client.GetAsync($"/api/admin/assistants/{id}/publication"));
        var persistedPublishedAt = publicationState.GetProperty("publishedAt").GetDateTime();
        Assert.Equal(DateTimeKind.Local, persistedPublishedAt.Kind);
        Assert.Equal(new DateTimeOffset(publishedAt).Offset,
            publicationState.GetProperty("publishedAt").GetDateTimeOffset().Offset);
        Assert.InRange(Math.Abs((persistedPublishedAt - publishedAt).TotalSeconds), 0, 1);

        using var anonymous = host.CreateClient();
        var bootstrap = await Data(await anonymous.GetAsync($"/api/public/assistants/{id}/bootstrap"));
        Assert.Equal("公开助手", bootstrap.GetProperty("displayName").GetString());
        Assert.Equal("发布时欢迎", bootstrap.GetProperty("welcomeMessage").GetString());
        await Data(await client.PutAsJsonAsync($"/api/admin/assistants/{id}/draft", new { name = "新草稿名称", expectedDraftRevision = 1, configuration = new { businessPolicyId = policy.GetProperty("id").GetString(), welcomeMessage = "未发布欢迎" } }));
        bootstrap = await Data(await anonymous.GetAsync($"/api/public/assistants/{id}/bootstrap"));
        Assert.Equal("发布时欢迎", bootstrap.GetProperty("welcomeMessage").GetString());

        var revoked = await Data(await client.PostAsJsonAsync($"/api/admin/assistants/{id}/revoke", new { expectedPointerRevision = 1 }));
        Assert.Equal(2, revoked.GetProperty("pointerRevision").GetInt64());
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/public/assistants/{id}/bootstrap")).StatusCode);
    }

    /// <summary>
    /// 验证知识问答和文档上传使用独立项目的存储。
    /// </summary>
    [Fact]
    public async Task Knowledge_questions_and_document_uploads_use_the_independent_storage()
    {
        using var host = new TestHost();
        using var client = host.CreateClient();
        var knowledge = await Data(await client.PostAsJsonAsync("/api/admin/knowledge-bases", new { name = "独立知识库", description = "本地文档" }));
        var id = knowledge.GetProperty("id").GetString();
        var question = await Data(await client.PutAsJsonAsync($"/api/admin/knowledge-bases/{id}/qna", new { question = "几点开始？", answer = "上午九点。", enabled = true, sortOrder = 1, showAsSuggestion = true }));
        Assert.Equal("上午九点。", question.GetProperty("answer").GetString());
        Assert.Single((await Data(await client.GetAsync($"/api/admin/knowledge-bases/{id}/qna"))).EnumerateArray());

        using var multipart = new MultipartFormDataContent();
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes("# 活动说明\n\n上午九点开始。"));
        content.Headers.ContentType = new MediaTypeHeaderValue("text/markdown");
        multipart.Add(content, "file", "guide.md");
        var document = await Data(await client.PostAsync($"/api/admin/knowledge-bases/{id}/documents", multipart));
        Assert.Equal("guide.md", document.GetProperty("fileName").GetString());
        var assetId = document.GetProperty("assetId").GetString();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/assets/{assetId}/content")).StatusCode);
        var documentId = document.GetProperty("id").GetString();
        var preview = await Data(await client.GetAsync($"/api/admin/knowledge-bases/{id}/documents/{documentId}/preview"));
        Assert.Contains("上午九点开始", preview.GetProperty("originalText").GetString());
        Assert.Single((await Data(await client.GetAsync($"/api/admin/knowledge-bases/{id}/documents"))).EnumerateArray());
        var listed = (await Data(await client.GetAsync("/api/admin/knowledge-bases"))).EnumerateArray().Single();
        Assert.Equal(1, listed.GetProperty("qnaCount").GetInt32());
        Assert.Equal(1, listed.GetProperty("documentCount").GetInt32());
    }

    /// <summary>
    /// 验证公开聊天持久化完整回答、流式返回内容并生成统计数据。
    /// </summary>
    [Fact]
    public async Task Public_chat_persists_a_completed_model_answer_streams_it_and_reports_analytics()
    {
        // Only the upstream HTTP transport is controlled. The actual provider
        // adapter parses this OpenAI SSE protocol; all app/DB/transport paths are real.
        using var providerFixture = new ChatProviderFixture();
        using var host = new TestHost(upstream: providerFixture);
        using var admin = host.CreateClient();
        var provider = await Data(await admin.PostAsJsonAsync("/api/admin/system/assistant-models/providers", new
        {
            expectedRevision = 0,
            settings = new { name = "受控上游协议", baseUrl = "https://fixture.example.test/v1", secretReference = "config://AssistantSecrets:FixtureChat", secretVersion = "v1", models = new[] { new { id = "fixture-chat", purpose = "chat", supportsTools = false } } }
        }));
        var policy = await Data(await admin.PostAsJsonAsync("/api/admin/assistant-models/policies", new
        {
            expectedRevision = 0,
            settings = new { name = "真实链路策略", businessCode = "chat_fixture", providerId = provider.GetProperty("id").GetString(), providerRevision = provider.GetProperty("revision").GetInt64(), model = "fixture-chat", instructions = "Answer the question.", knowledgeEnabled = false }
        }));
        var assistant = await Data(await admin.PostAsJsonAsync("/api/admin/assistants", new
        {
            name = "完整链路助手",
            configuration = new { businessPolicyId = policy.GetProperty("id").GetString(), welcomeMessage = "欢迎提问", anonymousChatEnabled = true }
        }));
        var id = assistant.GetProperty("id").GetString();
        await Data(await admin.PostAsJsonAsync($"/api/admin/assistants/{id}/publish", new { expectedDraftRevision = 1, expectedPointerRevision = 0 }));

        using var publicClient = host.CreateClient();
        var bootstrap = await Data(await publicClient.GetAsync($"/api/public/assistants/{id}/bootstrap"));
        Assert.Equal("完整链路助手", bootstrap.GetProperty("displayName").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await publicClient.PostAsJsonAsync($"/api/public/assistants/{id}/conversations", new { locale = "zh-cn" })).StatusCode);
        var session = await Data(await publicClient.PostAsJsonAsync($"/api/public/assistants/{id}/sessions", new { subjectHash = "fixture-browser", locale = "zh-cn" }));
        publicClient.DefaultRequestHeaders.Add("X-Assistant-Session-Id", session.GetProperty("session").GetProperty("id").GetString());
        publicClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-public-session-token");
        Assert.Equal(HttpStatusCode.Forbidden, (await publicClient.PostAsJsonAsync($"/api/public/assistants/{id}/conversations", new { locale = "zh-cn" })).StatusCode);
        publicClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("bearerToken").GetString());
        var conversation = await Data(await publicClient.PostAsJsonAsync($"/api/public/assistants/{id}/conversations", new { locale = "zh-cn" }));
        var conversationId = conversation.GetProperty("conversationId").GetString();
        var turn = await Data(await publicClient.PostAsJsonAsync($"/api/public/assistants/{id}/conversations/{conversationId}/turns", new { clientMessageId = "fixture-message-1", messageType = "text", text = ChatProviderFixture.Question, locale = "zh-cn" }));
        var runId = turn.GetProperty("runId").GetString();
        var ticket = Uri.EscapeDataString(turn.GetProperty("streamTicket").GetString()!);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var stream = await publicClient.GetAsync($"/api/public/assistants/{id}/runs/{runId}/events?conversationId={conversationId}&ticket={ticket}", HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        stream.EnsureSuccessStatusCode();
        Assert.Equal("text/event-stream", stream.Content.Headers.ContentType?.MediaType);
        var frames = await stream.Content.ReadAsStringAsync(deadline.Token);
        Assert.Contains("event: run.accepted", frames);
        Assert.Contains("event: message.delta", frames);
        Assert.Contains("event: message.completed", frames);
        Assert.Contains("event: run.completed", frames);
        Assert.Contains("data: [DONE]", frames);
        Assert.DoesNotContain("event: run.failed", frames);
        var envelopes = frames.Split('\n').Where(line => line.StartsWith("data: {")).Select(line => JsonDocument.Parse(line[6..]).RootElement.Clone()).ToArray();
        var completed = Assert.Single(envelopes, envelope => envelope.GetProperty("type").GetString() == "message.completed");
        Assert.Equal(ChatProviderFixture.Answer, completed.GetProperty("payload").GetProperty("text").GetString());
        Assert.Equal(1, providerFixture.Calls);

        var messages = await Data(await publicClient.GetAsync($"/api/public/assistants/{id}/conversations/{conversationId}/messages"));
        var items = messages.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(2, items.Length);
        var reply = Assert.Single(items, item => item.GetProperty("role").GetInt32() == 2);
        Assert.Equal(3, reply.GetProperty("status").GetInt32());
        Assert.Equal(3, reply.GetProperty("turnStatus").GetInt32());
        using var saved = JsonDocument.Parse(reply.GetProperty("contentJson").GetString()!);
        Assert.Equal(ChatProviderFixture.Answer, saved.RootElement.GetProperty("text").GetString());
        Assert.Equal("model", saved.RootElement.GetProperty("source").GetString());
        var conversations = await Data(await admin.GetAsync($"/api/admin/assistants/{id}/analytics/conversations"));
        Assert.Equal(1, conversations.GetProperty("totalCount").GetInt32());
        Assert.Equal(conversationId, Assert.Single(conversations.GetProperty("items").EnumerateArray()).GetProperty("conversationId").GetString());
        var summary = await Data(await admin.GetAsync($"/api/admin/assistants/{id}/analytics/summary"));
        Assert.Equal(1, summary.GetProperty("conversationCount").GetInt32());
        Assert.Equal(1, summary.GetProperty("completedTurnCount").GetInt32());
        Assert.Equal(1, summary.GetProperty("modelAnswerCount").GetInt32());
        Assert.Equal(0, summary.GetProperty("failedTurnCount").GetInt32());

        await Data(await admin.PostAsJsonAsync($"/api/admin/assistants/{id}/revoke", new { expectedPointerRevision = 1 }));
        Assert.Equal(HttpStatusCode.NotFound, (await publicClient.GetAsync($"/api/public/assistants/{id}/conversations/{conversationId}/messages")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await publicClient.PostAsJsonAsync($"/api/public/assistants/{id}/conversations", new { locale = "zh-cn" })).StatusCode);
    }

    /// <summary>
    /// 验证网页导入、HTML 解析、嵌入索引、发布和公开回答引用组成完整链路。
    /// </summary>
    [Fact]
    public async Task Imported_web_knowledge_is_indexed_and_cited_in_public_chat()
    {
        using var model = new KnowledgeProviderFixture();
        using var web = new WebPageFixture();
        using var host = new TestHost(upstream: model, webUpstream: web);
        using var admin = host.CreateClient();

        var provider = await Data(await admin.PostAsJsonAsync("/api/admin/system/assistant-models/providers", new
        {
            expectedRevision = 0,
            settings = new
            {
                name = "本地模拟模型",
                baseUrl = "https://fixture.example.com/v1",
                secretReference = "config://AssistantSecrets:FixtureChat",
                secretVersion = "v1",
                models = new object[]
                {
                    new { id = "fixture-chat", purpose = "chat", supportsTools = false },
                    new { id = "fixture-embedding", purpose = "embedding", supportsTools = false, dimension = 4 }
                }
            }
        }));
        var providerId = provider.GetProperty("id").GetString()!;
        var providerRevision = provider.GetProperty("revision").GetInt64();

        var knowledge = await Data(await admin.PostAsJsonAsync("/api/admin/knowledge-bases", new
        {
            name = "报名知识库",
            description = "本地端到端验收"
        }));
        var knowledgeId = knowledge.GetProperty("id").GetString()!;
        await Data(await admin.PutAsJsonAsync($"/api/admin/knowledge-bases/{knowledgeId}/embedding-profile", new
        {
            providerId,
            providerRevision,
            model = "fixture-embedding",
            dimension = 4,
            expectedRevision = 0
        }));

        var document = await Data(await admin.PostAsJsonAsync($"/api/admin/knowledge-bases/{knowledgeId}/web-pages", new
        {
            url = "https://docs.example.com/2026-registration"
        }));
        var documentId = document.GetProperty("id").GetString()!;
        Assert.Equal("https://docs.example.com/2026-registration", document.GetProperty("sourceUrl").GetString());
        Assert.Equal("2026 活动报名指南.html", document.GetProperty("fileName").GetString());

        var preview = await Data(await admin.GetAsync($"/api/admin/knowledge-bases/{knowledgeId}/documents/{documentId}/preview"));
        Assert.Contains("报名截止日期为 2026 年 12 月 31 日", preview.GetProperty("originalText").GetString());
        Assert.DoesNotContain("privateScriptMarker", preview.GetProperty("originalText").GetString());

        var processed = await Data(await admin.PostAsJsonAsync($"/api/admin/knowledge-bases/{knowledgeId}/documents/{documentId}/process", new { }));
        Assert.Equal("ready", processed.GetProperty("document").GetProperty("indexStatus").GetString());
        Assert.Equal((int)AssistantKnowledgeIndexJobStatus.Succeeded,
            processed.GetProperty("job").GetProperty("status").GetInt32());
        Assert.True(model.EmbeddingCalls > 0);

        var policy = await Data(await admin.PostAsJsonAsync("/api/admin/assistant-models/policies", new
        {
            expectedRevision = 0,
            settings = new
            {
                name = "知识库问答策略",
                businessCode = "registration_help",
                providerId,
                providerRevision,
                model = "fixture-chat",
                instructions = "请根据知识资料准确回答。",
                knowledgeEnabled = true,
                retrievalLimit = 5,
                requireEvidence = true,
                noAnswerMessage = "知识库中没有相关内容。"
            }
        }));
        var assistant = await Data(await admin.PostAsJsonAsync("/api/admin/assistants", new
        {
            name = "报名助手",
            configuration = new
            {
                businessPolicyId = policy.GetProperty("id").GetString(),
                knowledgeBaseId = knowledgeId,
                welcomeMessage = "欢迎咨询报名安排。",
                anonymousChatEnabled = true,
                capabilities = new { knowledgeQa = true, customQa = false, webSearch = false },
                retrieval = new { topK = 5, rerank = false }
            }
        }));
        var assistantId = assistant.GetProperty("id").GetString()!;
        await Data(await admin.PostAsJsonAsync($"/api/admin/assistants/{assistantId}/publish", new
        {
            expectedDraftRevision = 1,
            expectedPointerRevision = 0
        }));

        using var visitor = host.CreateClient();
        var browserSession = await Data(await visitor.PostAsJsonAsync(
            $"/api/public/assistants/{assistantId}/sessions",
            new { subjectHash = "local-mock-e2e", locale = "zh-cn" }));
        visitor.DefaultRequestHeaders.Add("X-Assistant-Session-Id",
            browserSession.GetProperty("session").GetProperty("id").GetString());
        visitor.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", browserSession.GetProperty("bearerToken").GetString());
        var conversation = await Data(await visitor.PostAsJsonAsync(
            $"/api/public/assistants/{assistantId}/conversations", new { locale = "zh-cn" }));
        var conversationId = conversation.GetProperty("conversationId").GetString()!;
        var turn = await Data(await visitor.PostAsJsonAsync(
            $"/api/public/assistants/{assistantId}/conversations/{conversationId}/turns",
            new { clientMessageId = "local-mock-e2e-message", messageType = "text", text = KnowledgeProviderFixture.Question, locale = "zh-cn" }));
        var runId = turn.GetProperty("runId").GetString()!;
        var ticket = Uri.EscapeDataString(turn.GetProperty("streamTicket").GetString()!);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var stream = await visitor.GetAsync(
            $"/api/public/assistants/{assistantId}/runs/{runId}/events?conversationId={conversationId}&ticket={ticket}",
            HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        stream.EnsureSuccessStatusCode();
        var frames = await stream.Content.ReadAsStringAsync(deadline.Token);
        var envelopes = frames.Split('\n')
            .Where(line => line.StartsWith("data: {", StringComparison.Ordinal))
            .Select(line => JsonDocument.Parse(line[6..]).RootElement.Clone())
            .ToArray();
        var completed = Assert.Single(envelopes, value => value.GetProperty("type").GetString() == "message.completed");
        Assert.Equal(KnowledgeProviderFixture.Answer, completed.GetProperty("payload").GetProperty("text").GetString());
        var citation = Assert.Single(completed.GetProperty("payload").GetProperty("citations").EnumerateArray());
        Assert.Equal("2026 活动报名指南.html", citation.GetProperty("documentName").GetString());
        Assert.Equal("https://docs.example.com/2026-registration", citation.GetProperty("sourceUrl").GetString());
        Assert.Contains("报名截止日期为 2026 年 12 月 31 日", citation.GetProperty("excerpt").GetString());
        Assert.Contains("报名截止日期为 2026 年 12 月 31 日", model.LastSystemMessage, StringComparison.Ordinal);

        var messages = await Data(await visitor.GetAsync(
            $"/api/public/assistants/{assistantId}/conversations/{conversationId}/messages"));
        var savedAnswer = Assert.Single(messages.GetProperty("items").EnumerateArray(),
            value => value.GetProperty("role").GetInt32() == 2);
        using var savedBody = JsonDocument.Parse(savedAnswer.GetProperty("contentJson").GetString()!);
        Assert.Equal(1, savedBody.RootElement.GetProperty("citations").GetArrayLength());
        Assert.Equal(1, model.ChatCalls);
    }

    /// <summary>
    /// 确认 HTTP 响应成功并拆取统一业务响应中的返回对象。
    /// </summary>
    private static async Task<JsonElement> Data(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"HTTP {(int)response.StatusCode}: {json}");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.True(root.GetProperty("isSuccess").GetBoolean());
        Assert.Equal(0, root.GetProperty("businessCode").GetInt32());
        return root.GetProperty("returnObj").Clone();
    }

    /// <summary>
    /// 仅供独立项目测试使用的测试辅助类型。
    /// </summary>
    private sealed class TestHost(
        string? directory = null,
        HttpMessageHandler? upstream = null,
        HttpMessageHandler? webUpstream = null) : WebApplicationFactory<Program>
    {
        /// <summary>
        /// 保存测试在处理目录时所需的数据或状态。
        /// </summary>
        private readonly string _directory = directory ?? NewDirectory();
        /// <summary>
        /// 在测试输出目录下创建隔离的临时数据路径。
        /// </summary>
        public static string NewDirectory() => Path.Combine(AppContext.BaseDirectory, "TestResults", "test-data", Guid.NewGuid().ToString("N"));
        /// <summary>
        /// 验证网络相关行为、预期结果及错误边界。
        /// </summary>
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Test");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Assistant:DataDirectory"] = _directory,
                ["AssistantSecrets:FixtureChat"] = ChatProviderFixture.TestToken
            }));
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IStartupFilter>(new NoUserIdentityFilter());
                if (upstream is not null)
                    services.AddHttpClient("assistant-provider").ConfigurePrimaryHttpMessageHandler(() => upstream);
                if (webUpstream is not null)
                    services.AddHttpClient("assistant-web-import").ConfigurePrimaryHttpMessageHandler(() => webUpstream);
            });
        }
    }

    /// <summary>
    /// 在真实请求处理结束后确认宿主没有注入任何固定用户或身份声明。
    /// </summary>
    private sealed class NoUserIdentityFilter : IStartupFilter
    {
        /// <summary>
        /// 包装实际应用管线，所有管理及公开聊天请求均应保持无用户身份。
        /// </summary>
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
            => app =>
            {
                app.Use(async (context, nextMiddleware) =>
                {
                    await nextMiddleware();
                    Assert.False(context.User.Identity?.IsAuthenticated == true);
                    Assert.Empty(context.User.Claims);
                });
                next(app);
            };
    }

    /// <summary>
    /// 为独立项目测试相关测试提供隔离数据、受控依赖或固定 HTTP 响应。
    /// </summary>
    private sealed class ChatProviderFixture : HttpMessageHandler
    {
        /// <summary>
        /// 定义聊天模型供应商测试夹具使用的测试令牌配置值。
        /// </summary>
        public const string TestToken = "fixture-only-no-real-provider-key";
        /// <summary>
        /// 定义聊天模型供应商测试夹具使用的问题配置值。
        /// </summary>
        public const string Question = "What does the standalone assistant verify?";
        /// <summary>
        /// 定义聊天模型供应商测试夹具使用的回答配置值。
        /// </summary>
        public const string Answer = "The standalone assistant persists and streams this verified answer.";
        /// <summary>
        /// 表示聊天模型供应商测试夹具的调用信息。
        /// </summary>
        public int Calls { get; private set; }
        /// <summary>
        /// 验证发送相关行为、预期结果及错误边界。
        /// </summary>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://fixture.example.test/v1/chat/completions", request.RequestUri?.ToString());
            Assert.Equal(TestToken, request.Headers.Authorization?.Parameter);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Assert.Equal("fixture-chat", body.RootElement.GetProperty("model").GetString());
            Assert.True(body.RootElement.GetProperty("stream").GetBoolean());
            Assert.Contains(body.RootElement.GetProperty("messages").EnumerateArray(), message => message.GetProperty("role").GetString() == "user" && message.GetProperty("content").GetString() == Question);
            Calls++;
            var chunk = JsonSerializer.Serialize(new { id = "fixture-completion", choices = new[] { new { index = 0, delta = new { content = Answer }, finish_reason = (string?)null } } });
            var done = JsonSerializer.Serialize(new { id = "fixture-completion", choices = new[] { new { index = 0, delta = new { }, finish_reason = "stop" } }, usage = new { prompt_tokens = 12, completion_tokens = 10, total_tokens = 22 } });
            return new(HttpStatusCode.OK) { Content = new StringContent($"data: {chunk}\n\ndata: {done}\n\ndata: [DONE]\n\n", Encoding.UTF8, "text/event-stream") };
        }
    }

    /// <summary>
    /// 为完整知识问答链路提供确定性的 OpenAI 兼容聊天和嵌入响应。
    /// </summary>
    private sealed class KnowledgeProviderFixture : HttpMessageHandler
    {
        /// <summary>
        /// 定义端到端验收使用的问题配置值。
        /// </summary>
        public const string Question = "报名截止日期是什么？";
        /// <summary>
        /// 定义端到端验收使用的模型回答配置值。
        /// </summary>
        public const string Answer = "报名截止日期为 2026 年 12 月 31 日。";
        /// <summary>
        /// 表示模拟嵌入服务接收的请求数量。
        /// </summary>
        public int EmbeddingCalls { get; private set; }
        /// <summary>
        /// 表示模拟聊天服务接收的请求数量。
        /// </summary>
        public int ChatCalls { get; private set; }
        /// <summary>
        /// 保存最近一次聊天请求的系统提示词，供检索上下文断言使用。
        /// </summary>
        public string LastSystemMessage { get; private set; } = string.Empty;

        /// <summary>
        /// 根据请求路径返回确定性嵌入向量或流式聊天完成事件。
        /// </summary>
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(ChatProviderFixture.TestToken, request.Headers.Authorization?.Parameter);
            var path = request.RequestUri?.AbsolutePath;
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));

            if (path?.EndsWith("/embeddings", StringComparison.Ordinal) == true)
            {
                Assert.Equal("fixture-embedding", body.RootElement.GetProperty("model").GetString());
                var inputCount = body.RootElement.GetProperty("input").GetArrayLength();
                EmbeddingCalls++;
                var vectors = Enumerable.Range(0, inputCount)
                    .Select(index => new { index, embedding = new[] { 1f, 0f, 0f, 0f } })
                    .ToArray();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { data = vectors, model = "fixture-embedding" })
                };
            }

            Assert.EndsWith("/chat/completions", path, StringComparison.Ordinal);
            Assert.Equal("fixture-chat", body.RootElement.GetProperty("model").GetString());
            Assert.True(body.RootElement.GetProperty("stream").GetBoolean());
            var messages = body.RootElement.GetProperty("messages").EnumerateArray().ToArray();
            LastSystemMessage = messages.First(value => value.GetProperty("role").GetString() == "system")
                .GetProperty("content").GetString()!;
            Assert.Contains(Question, messages.Select(value => value.GetProperty("content").GetString()), StringComparer.Ordinal);
            ChatCalls++;

            var chunk = JsonSerializer.Serialize(new
            {
                id = "knowledge-fixture-completion",
                choices = new[] { new { index = 0, delta = new { content = Answer }, finish_reason = (string?)null } }
            });
            var done = JsonSerializer.Serialize(new
            {
                id = "knowledge-fixture-completion",
                choices = new[] { new { index = 0, delta = new { }, finish_reason = "stop" } }
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $"data: {chunk}\n\ndata: {done}\n\ndata: [DONE]\n\n",
                    Encoding.UTF8, "text/event-stream")
            };
        }
    }

    /// <summary>
    /// 为网页导入流程提供固定 HTML，避免测试访问公网。
    /// </summary>
    private sealed class WebPageFixture : HttpMessageHandler
    {
        /// <summary>
        /// 定义模拟网页中的正文内容配置值。
        /// </summary>
        public const string Html = """
            <!doctype html><html><head><title>2026 活动报名指南</title></head>
            <body><nav>导航栏不应进入知识库</nav><main><h1>报名安排</h1>
            <p>本项目报名截止日期为 2026 年 12 月 31 日，请通过活动官网提交申请。</p>
            </main><script>privateScriptMarker()</script></body></html>
            """;

        /// <summary>
        /// 返回模拟公开网页内容，并确认测试请求的目标地址。
        /// </summary>
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal("https://docs.example.com/2026-registration", request.RequestUri?.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Html, Encoding.UTF8, "text/html")
            });
        }
    }
}
