using System.Data;
using Microsoft.Extensions.Configuration;
using AI.Assistant.Services.Contracts;
using AI.Assistant.Services.Domain;
using AI.Assistant.Services.Infrastructure;
using AI.Assistant.Persistence;
using SqlSugar;
using Xunit;
using AI.Assistant.Models.Runtime;

namespace AI.Assistant.Tests;

/// <summary>
/// 验证当前数据库结构、物理删除、消息顺序和工具耗时统计。
/// </summary>
public sealed class AssistantPersistenceSchemaTests
{
    /// <summary>
    /// 验证新建数据库不包含审计、范围及用户身份字段，同时保留业务时间和乐观锁字段。
    /// </summary>
    [Fact]
    public void Fresh_database_omits_audit_and_scope_columns_and_keeps_business_state()
    {
        var config = Configuration();
        var directory = LocalDatabase.DataDirectory(config);
        LocalDatabase.Migrate(directory);
        LocalDatabase.Migrate(directory);
        using var db = LocalDatabase.Open(directory);
        Assert.Equal(1, db.Ado.GetInt("SELECT COUNT(*) FROM assistant_schema_migration"));
        Assert.Equal("001_initial.sql", db.Ado.GetString("SELECT version FROM assistant_schema_migration"));
        Assert.Equal(23, db.Ado.GetInt("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name <> 'assistant_schema_migration'"));
        var removed = new[]
        {
            "is_deleted", "deleted_at", "deleted_by", "created_at", "created_by",
            "updated_at", "updated_by", "tenant_id", "org_id",
            "actor_id", "published_by", "revoked_by"
        };
        foreach (DataRow table in db.Ado.GetDataTable("SELECT name FROM sqlite_master WHERE type = 'table'").Rows)
        {
            var names = db.Ado.GetDataTable($"SELECT name FROM pragma_table_info('{table["name"]}')").Rows.Cast<DataRow>()
                .Select(row => row["name"].ToString()).ToArray();
            Assert.Empty(names.Intersect(removed));
        }
        Assert.Equal(1, db.Ado.GetInt("SELECT COUNT(*) FROM pragma_table_info('assistant_draft') WHERE name = 'draft_revision'"));
        Assert.Equal(2, db.Ado.GetInt("SELECT COUNT(*) FROM pragma_table_info('assistant_run') WHERE name IN ('started_at', 'completed_at')"));
        Assert.Empty(db.Ado.GetDataTable("PRAGMA foreign_key_check").Rows.Cast<DataRow>());
    }

    /// <summary>
    /// 验证消息使用写入顺序分页，随机标识及角色不会打乱同一轮次的消息。
    /// </summary>
    [Fact]
    public async Task Messages_keep_insertion_order_and_deleting_session_cascades_to_runtime_children()
    {
        var config = Configuration();
        LocalDatabase.Migrate(LocalDatabase.DataDirectory(config));
        using var db = new LocalDbContext(config);
        var client = db.GetSugarScope();
        InsertRuntime(client);
        Insert(client, "assistant_message", "z-user", ("conversation_id", "conversation"), ("turn_id", "turn"), ("role", 1));
        Insert(client, "assistant_message", "y-assistant", ("conversation_id", "conversation"), ("turn_id", "turn"), ("run_id", "run"), ("role", 2));
        Insert(client, "assistant_message", "x-tool", ("conversation_id", "conversation"), ("turn_id", "turn"), ("run_id", "run"), ("role", 3));
        Insert(client, "assistant_message", "a-assistant", ("conversation_id", "conversation"), ("turn_id", "turn"), ("run_id", "run"), ("role", 2));
        Insert(client, "assistant_tool_call", "tool", ("run_id", "run"));
        var runtime = new AssistantRuntimeRepository(db);
        var scope = new AssistantScopeSnapshot("assistant", "published", AssistantExecutionMode.Public);
        Assert.Equal(new[] { "z-user", "y-assistant", "x-tool", "a-assistant" },
            (await runtime.ListMessagesByTurnAsync(scope, "turn", CancellationToken.None)).Select(message => message.Id));
        var analytics = new AssistantAnalyticsRepository(db);
        var first = await analytics.GetMessagesAsync("assistant", "conversation", 2, null, CancellationToken.None);
        Assert.Equal(new[] { "z-user", "y-assistant" }, first.Items.Select(message => message.Id));
        var second = await analytics.GetMessagesAsync("assistant", "conversation", 2, first.NextCursor, CancellationToken.None);
        Assert.Equal("a-assistant", Assert.Single(second.Items).Id);
        foreach (var otherScope in new[]
        {
            scope with { AssistantId = "other-assistant" },
            scope with { PublishedVersionId = "other-version" },
            scope with { ExecutionMode = AssistantExecutionMode.Preview }
        })
        {
            Assert.Null(await runtime.GetSessionAsync(otherScope, "session", CancellationToken.None));
            Assert.Equal(0, await runtime.DeleteSessionAsync(otherScope, "session", CancellationToken.None));
        }
        Assert.Equal(1, await runtime.DeleteSessionAsync(scope, "session", CancellationToken.None));
        foreach (var table in new[] { "assistant_conversation", "assistant_turn", "assistant_run", "assistant_message", "assistant_tool_call" })
            Assert.Equal(0, client.Ado.GetInt($"SELECT COUNT(*) FROM {table}"));
        Assert.ThrowsAny<Exception>(() => Insert(client, "assistant_tool_call", "late-tool", ("run_id", "run")));
    }

    /// <summary>
    /// 验证物理删除知识库会清理所有文档和索引后代，问答可多次删除后重建。
    /// </summary>
    [Fact]
    public async Task Knowledge_deletion_cascades_and_repeated_question_recreation_is_allowed()
    {
        var config = Configuration();
        LocalDatabase.Migrate(LocalDatabase.DataDirectory(config));
        using var db = new LocalDbContext(config);
        var client = db.GetSugarScope();
        Insert(client, "assistant_knowledge_base", "base");
        InsertKnowledgeChildren(client, "base");
        var repository = new AssistantKnowledgeRepository(db);
        for (var i = 0; i < 3; i++)
        {
            var id = $"qna-{i}";
            Insert(client, "assistant_knowledge_qna", id, ("knowledge_base_id", "base"), ("normalized_question", "相同问题"));
            await repository.DeleteQnaAsync("base", id, CancellationToken.None);
            await db.CommitAsync();
            Assert.Equal(0, client.Ado.GetInt("SELECT COUNT(*) FROM assistant_knowledge_qna"));
        }
        await repository.DeleteBaseAsync("base", CancellationToken.None);
        await db.CommitAsync();
        foreach (var table in KnowledgeChildren.Append("assistant_knowledge_base"))
            Assert.Equal(0, client.Ado.GetInt($"SELECT COUNT(*) FROM {table}"));
    }

    /// <summary>
    /// 验证草稿删除释放幂等键，旧实例的后续保存不能恢复已删除的数据。
    /// </summary>
    [Fact]
    public async Task Draft_deletion_releases_idempotency_key_and_stale_save_cannot_restore_it()
    {
        var config = Configuration();
        LocalDatabase.Migrate(LocalDatabase.DataDirectory(config));
        using var db = new LocalDbContext(config);
        var repository = new AssistantDraftRepository(db);
        var draft = AssistantDraft.Create("old", "助手", null, "{}", "key");
        await repository.AddAsync(draft, CancellationToken.None);
        await db.CommitAsync();
        await repository.DeleteAsync(draft.Id, CancellationToken.None);
        await db.CommitAsync();
        Assert.Null(await repository.GetByIdempotencyKeyAsync("key", CancellationToken.None));
        draft.Save("过期副本", null, "{}", 1);
        await repository.SaveAsync(draft, 1, CancellationToken.None);
        await Assert.ThrowsAsync<AssistantDraftRepository.DraftRevisionConflictException>(() => db.CommitAsync());
        var replacement = AssistantDraft.Create("new", "新助手", null, "{}", "key");
        await repository.AddAsync(replacement, CancellationToken.None);
        await db.CommitAsync();
        Assert.Equal("new", (await repository.GetByIdempotencyKeyAsync("key", CancellationToken.None))!.Id);
    }

    /// <summary>
    /// 验证工具耗时可以持久化并参与统计，无需保存创建时间。
    /// </summary>
    [Fact]
    public async Task Tool_duration_round_trips_and_drives_analytics()
    {
        var config = Configuration();
        LocalDatabase.Migrate(LocalDatabase.DataDirectory(config));
        using var db = new LocalDbContext(config);
        InsertRuntime(db.GetSugarScope());
        var runtime = new AssistantRuntimeRepository(db);
        var tool = AssistantToolCall.Create("tool", "run", "search", "v1", "hash");
        tool.Start();
        tool.Complete("{}", DateTime.Now);
        tool.RecordDuration(TimeSpan.FromMilliseconds(125));
        await runtime.AddToolCallAsync(tool, CancellationToken.None);
        await db.CommitAsync();
        var scope = new AssistantScopeSnapshot("assistant", "published", AssistantExecutionMode.Public);
        Assert.Equal(125, (await runtime.GetToolCallAsync(scope, tool.Id, CancellationToken.None))!.DurationMs);
        var counts = await new AssistantAnalyticsRepository(db).GetCountsAsync("assistant", CancellationToken.None);
        Assert.Equal(125, counts.AverageToolLatencyMs);
    }

    /// <summary>
    /// 验证提交时仍在发布的草稿不能删除，撤回后删除会清理发布入口及运行数据。
    /// </summary>
    [Fact]
    public async Task Draft_deletion_checks_publication_in_transaction_and_cleans_related_data()
    {
        var config = Configuration();
        LocalDatabase.Migrate(LocalDatabase.DataDirectory(config));
        using var db = new LocalDbContext(config);
        var client = db.GetSugarScope();
        Insert(client, "assistant_draft", "assistant");
        InsertRuntime(client);
        Insert(client, "assistant_published_version", "published");
        Insert(client, "assistant_publication_pointer", "pointer", ("status", 1), ("current_published_version_id", "published"));
        Insert(client, "assistant_preview_snapshot", "preview");
        var repository = new AssistantDraftRepository(db);
        await repository.DeleteAsync("assistant", CancellationToken.None);
        await Assert.ThrowsAsync<AI.Assistant.Platform.BizResultException>(() => db.CommitAsync());
        Assert.Equal(1, client.Ado.GetInt("SELECT COUNT(*) FROM assistant_anonymous_session"));
        client.Ado.ExecuteCommand("UPDATE assistant_publication_pointer SET status = 2");
        await repository.DeleteAsync("assistant", CancellationToken.None);
        await db.CommitAsync();
        foreach (var table in new[] { "assistant_draft", "assistant_anonymous_session", "assistant_conversation", "assistant_turn",
            "assistant_run", "assistant_published_version", "assistant_publication_pointer", "assistant_preview_snapshot" })
            Assert.Equal(0, client.Ado.GetInt($"SELECT COUNT(*) FROM {table}"));
    }

    /// <summary>
    /// 列出由知识库物理删除触发级联清理的业务表。
    /// </summary>
    private static readonly string[] KnowledgeChildren =
    [
        "assistant_knowledge_document", "assistant_knowledge_document_version", "assistant_knowledge_chunk",
        "assistant_knowledge_chunking_policy", "assistant_knowledge_index_job", "assistant_knowledge_embedding_policy"
    ];

    /// <summary>
    /// 建立真实数据库查询所需的完整会话、对话、轮次和运行关联。
    /// </summary>
    private static void InsertRuntime(ISqlSugarClient db)
    {
        Insert(db, "assistant_anonymous_session", "session");
        Insert(db, "assistant_conversation", "conversation", ("session_id", "session"));
        Insert(db, "assistant_turn", "turn", ("conversation_id", "conversation"), ("session_id", "session"), ("turn_no", 1));
        Insert(db, "assistant_run", "run", ("turn_id", "turn"), ("conversation_id", "conversation"), ("session_id", "session"));
    }

    /// <summary>
    /// 建立文档、版本、片段、分块策略、索引任务及嵌入策略的级联测试数据。
    /// </summary>
    private static void InsertKnowledgeChildren(ISqlSugarClient db, string baseId)
    {
        Insert(db, "assistant_knowledge_document", "document", ("knowledge_base_id", baseId));
        Insert(db, "assistant_knowledge_document_version", "version", ("document_id", "document"));
        Insert(db, "assistant_knowledge_chunk", "chunk", ("document_version_id", "version"));
        Insert(db, "assistant_knowledge_chunking_policy", "chunk-policy", ("knowledge_base_id", baseId));
        Insert(db, "assistant_knowledge_index_job", "index-job", ("knowledge_base_id", baseId), ("document_id", "document"), ("document_version_id", "version"));
        Insert(db, "assistant_knowledge_embedding_policy", "embedding-policy", ("knowledge_base_id", baseId));
    }

    /// <summary>
    /// 根据当前数据库结构填充必填值，并允许测试覆盖指定字段。
    /// </summary>
    private static void Insert(ISqlSugarClient db, string table, string id, params (string Name, object Value)[] overrides)
    {
        var values = new Dictionary<string, object>
        {
            ["id"] = id,
            ["assistant_id"] = "assistant",
            ["published_version_id"] = "published",
            ["execution_mode"] = 1
        };
        foreach (var (name, value) in overrides) values[name] = value;
        var columns = db.Ado.GetDataTable($"""
            SELECT name, type, [notnull],
                CASE WHEN dflt_value IS NULL THEN 0 ELSE 1 END AS has_default
            FROM pragma_table_info('{table}')
            """)
            .Rows.Cast<DataRow>().ToArray();
        foreach (var column in columns)
        {
            var name = column["name"].ToString()!;
            if (values.ContainsKey(name) || Convert.ToInt32(column["notnull"]) == 0 || Convert.ToInt32(column["has_default"]) == 1) continue;
            values[name] = column["type"].ToString() == "INTEGER" ? 1
                : name.EndsWith("_at", StringComparison.Ordinal) ? "2026-11-01T00:00:00Z"
                : name.EndsWith("_json", StringComparison.Ordinal) ? "{}" : "fixture";
        }
        var existing = columns.Select(row => row["name"].ToString()!).ToHashSet();
        var parameters = values.Where(pair => existing.Contains(pair.Key))
            .Select(pair => new SugarParameter("@" + pair.Key, pair.Value)).ToArray();
        var names = parameters.Select(parameter => parameter.ParameterName[1..]);
        db.Ado.ExecuteCommand($"""
            INSERT INTO {table} ({string.Join(", ", names)})
            VALUES ({string.Join(", ", parameters.Select(parameter => parameter.ParameterName))})
            """, parameters);
    }

    /// <summary>
    /// 为每个测试分配独立目录，避免修改实际运行数据库。
    /// </summary>
    private static IConfiguration Configuration()
        => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Assistant:DataDirectory"] = Path.Combine(AppContext.BaseDirectory, "TestResults", "test-data", Guid.NewGuid().ToString("N"))
        }).Build();
}
