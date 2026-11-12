using System.Data;
using Microsoft.Extensions.Configuration;
using SmartAssistant.Modules.Contracts;
using SmartAssistant.Modules.Domain;
using SmartAssistant.Modules.Infrastructure;
using SmartAssistant.Persistence;
using SqlSugar;
using Xunit;

namespace SmartAssistant.Tests;

/// <summary>
/// 验证通用字段移除后的结构升级、物理删除、消息顺序和工具耗时统计。
/// </summary>
public sealed class AssistantAuditRemovalTests
{
    /// <summary>
    /// 验证新建数据库不包含审计、租户及组织字段，同时保留业务时间和乐观锁字段。
    /// </summary>
    [Fact]
    public void Fresh_database_omits_audit_and_scope_columns_and_keeps_business_state()
    {
        var config = Configuration();
        var directory = LocalDatabase.DataDirectory(config);
        LocalDatabase.Migrate(directory);
        using var db = LocalDatabase.Open(directory);
        var removed = new[]
        {
            "is_deleted", "deleted_at", "deleted_by", "created_at", "created_by",
            "updated_at", "updated_by", "tenant_id", "org_id"
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
    /// 验证仅应用初始迁移的旧数据库可升级，软删除父记录的后代不重新出现。
    /// </summary>
    [Fact]
    public async Task Upgrade_removes_deleted_families_and_preserves_live_data_preview_vectors_and_latency()
    {
        var config = Configuration();
        var directory = LocalDatabase.DataDirectory(config);
        var collection = AssistantKnowledgeIndexNaming.Collection("live-base", "profile", 2);
        var legacyCollection = "assistant_kb_0123456789ab_abcdef012345_" + collection[13..];
        using (var legacy = LocalDatabase.Open(directory))
        {
            var initial = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Migrations", "001_initial.sql"));
            foreach (var command in initial.Split(';', StringSplitOptions.RemoveEmptyEntries))
                if (!string.IsNullOrWhiteSpace(command)) legacy.Ado.ExecuteCommand(command);
            legacy.Ado.ExecuteCommand("CREATE TABLE assistant_schema_migration (version TEXT PRIMARY KEY, sha256 TEXT NOT NULL, applied_at TEXT NOT NULL)");
            legacy.Ado.ExecuteCommand("""
                INSERT INTO assistant_schema_migration VALUES (
                    '001_initial.sql',
                    '7a539c15b302e6f14dfd829dea61295de38de377bd687ccabd5d0cc8f4eeb148',
                    '2026-11-01'
                )
                """);
            Insert(legacy, "assistant_draft", "assistant");
            Insert(legacy, "assistant_knowledge_base", "deleted-base", ("is_deleted", 1));
            InsertKnowledgeChildren(legacy, "deleted-base");
            Insert(legacy, "assistant_knowledge_base", "live-base", ("name", "保留知识库"));
            Insert(legacy, "assistant_knowledge_qna", "live-qna", ("knowledge_base_id", "live-base"), ("answer", "保留回答"));
            InsertRuntime(legacy);
            Insert(legacy, "assistant_tool_call", "tool", ("run_id", "run"), ("status", 3),
                ("created_at", "2026-11-01T00:00:00.000Z"), ("completed_at", "2026-11-01T00:00:00.250Z"));
            Insert(legacy, "assistant_anonymous_session", "deleted-session", ("is_deleted", 1));
            Insert(legacy, "assistant_conversation", "deleted-conversation", ("session_id", "deleted-session"));
            Insert(legacy, "assistant_preview_snapshot", "preview",
                ("expires_at", "2099-11-01T00:00:00Z"),
                ("configuration_json", "{\"welcomeMessage\":\"保留预览\"}"),
                ("session_id", "session"), ("conversation_id", "conversation"));
            legacy.Ado.ExecuteCommand("INSERT INTO assistant_vector_collection VALUES (@name, 2)",
                new SugarParameter("@name", legacyCollection));
            legacy.Ado.ExecuteCommand("""
                INSERT INTO assistant_vector_point
                    (collection_name, chunk_id, content_hash, document_id, version_no, chunk_index, vector_json)
                VALUES (@name, 'live-chunk', 'hash', 'live-document', 1, 0, '[1,0]')
                """, new SugarParameter("@name", legacyCollection));
        }

        LocalDatabase.Migrate(directory);
        LocalDatabase.Migrate(directory);
        using var upgraded = LocalDatabase.Open(directory);
        Assert.Equal("保留知识库", upgraded.Ado.GetString("SELECT name FROM assistant_knowledge_base WHERE id = 'live-base'"));
        Assert.Equal("保留回答", upgraded.Ado.GetString("SELECT answer FROM assistant_knowledge_qna WHERE id = 'live-qna'"));
        Assert.Equal(0, upgraded.Ado.GetInt("SELECT COUNT(*) FROM assistant_conversation WHERE id = 'deleted-conversation'"));
        foreach (var table in KnowledgeChildren)
            Assert.Equal(0, upgraded.Ado.GetInt($"SELECT COUNT(*) FROM {table}"));
        Assert.InRange(Convert.ToDouble(upgraded.Ado.GetScalar("SELECT duration_ms FROM assistant_tool_call WHERE id = 'tool'")), 249, 251);
        Assert.Equal("2099-11-01T00:00:00Z", upgraded.Ado.GetString("SELECT expires_at FROM assistant_preview_snapshot WHERE id = 'preview'"));
        Assert.Equal("{\"welcomeMessage\":\"保留预览\"}", upgraded.Ado.GetString("SELECT configuration_json FROM assistant_preview_snapshot WHERE id = 'preview'"));
        Assert.Equal("conversation", upgraded.Ado.GetString("SELECT conversation_id FROM assistant_preview_snapshot WHERE id = 'preview'"));
        Assert.Equal(0, upgraded.Ado.GetInt("SELECT COUNT(*) FROM assistant_vector_collection WHERE name = @name",
            new SugarParameter("@name", legacyCollection)));
        var hits = await new LocalVectorIndex(config).SearchAsync(collection, new float[] { 1, 0 }, 5, CancellationToken.None);
        Assert.Equal("live-chunk", Assert.Single(hits).ChunkId);
        Assert.Empty(upgraded.Ado.GetDataTable("PRAGMA foreign_key_check").Rows.Cast<DataRow>());
    }

    /// <summary>
    /// 验证审计迁移的新旧校验值均可升级，旧版本遗漏的预览过期列会自动补齐。
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Upgrade_from_audit_migration_preserves_data_and_repairs_legacy_preview_schema(bool legacyPreviewSchema)
    {
        var directory = LocalDatabase.DataDirectory(Configuration());
        using (var db = LocalDatabase.Open(directory))
        {
            ApplyThroughAuditMigration(db);
            Insert(db, "assistant_draft", "assistant");
            if (legacyPreviewSchema)
            {
                db.Ado.ExecuteCommand("ALTER TABLE assistant_preview_snapshot DROP COLUMN expires_at");
                db.Ado.ExecuteCommand("""
                    UPDATE assistant_schema_migration
                    SET sha256 = '1dded6cb573d571245611f61b70321e97afabc8222a6357b4fefe6975361c836'
                    WHERE version = '002_remove_audit_fields.sql'
                    """);
            }
            else
            {
                Insert(db, "assistant_preview_snapshot", "preview", ("expires_at", "2099-01-01T00:00:00Z"));
            }
        }

        LocalDatabase.Migrate(directory);
        LocalDatabase.Migrate(directory);
        using var upgraded = LocalDatabase.Open(directory);
        Assert.Equal(1, upgraded.Ado.GetInt("SELECT COUNT(*) FROM assistant_draft WHERE id = 'assistant'"));
        Assert.Equal(1, upgraded.Ado.GetInt("SELECT COUNT(*) FROM pragma_table_info('assistant_preview_snapshot') WHERE name = 'expires_at'"));
        if (!legacyPreviewSchema)
            Assert.Equal("2099-01-01T00:00:00Z", upgraded.Ado.GetString("SELECT expires_at FROM assistant_preview_snapshot WHERE id = 'preview'"));
        Assert.Equal(0, upgraded.Ado.GetInt("SELECT COUNT(*) FROM pragma_table_info('assistant_draft') WHERE name IN ('tenant_id', 'org_id')"));
        Assert.Empty(upgraded.Ado.GetDataTable("PRAGMA foreign_key_check").Rows.Cast<DataRow>());
    }

    /// <summary>
    /// 验证取消旧范围后若幂等键冲突，迁移完整回滚并保留原始数据和约束。
    /// </summary>
    [Fact]
    public void Conflicting_legacy_idempotency_keys_roll_back_scope_removal_without_losing_data()
    {
        var directory = LocalDatabase.DataDirectory(Configuration());
        using (var db = LocalDatabase.Open(directory))
        {
            ApplyThroughAuditMigration(db);
            Insert(db, "assistant_draft", "one", ("tenant_id", "one"), ("creation_idempotency_key", "same-key"));
            Insert(db, "assistant_draft", "two", ("tenant_id", "two"), ("creation_idempotency_key", "same-key"));
        }

        Assert.ThrowsAny<Exception>(() => LocalDatabase.Migrate(directory));
        using var unchanged = LocalDatabase.Open(directory);
        Assert.Equal(2, unchanged.Ado.GetInt("SELECT COUNT(*) FROM assistant_draft"));
        Assert.Equal(2, unchanged.Ado.GetInt("SELECT COUNT(*) FROM pragma_table_info('assistant_draft') WHERE name IN ('tenant_id', 'org_id')"));
        Assert.Equal(0, unchanged.Ado.GetInt("SELECT COUNT(*) FROM assistant_schema_migration WHERE version = '003_remove_tenant_org_fields.sql'"));
        Assert.ThrowsAny<Exception>(() => Insert(unchanged, "assistant_draft", "duplicate",
            ("tenant_id", "one"), ("creation_idempotency_key", "same-key")));
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
        var draft = AssistantDraft.Create("old", "助手", null, "{}", "actor", "key");
        await repository.AddAsync(draft, CancellationToken.None);
        await db.CommitAsync();
        await repository.DeleteAsync(draft.Id, CancellationToken.None);
        await db.CommitAsync();
        Assert.Null(await repository.GetByIdempotencyKeyAsync("key", CancellationToken.None));
        draft.Save("过期副本", null, "{}", 1, "actor");
        await repository.SaveAsync(draft, 1, CancellationToken.None);
        await Assert.ThrowsAsync<AssistantDraftRepository.DraftRevisionConflictException>(() => db.CommitAsync());
        var replacement = AssistantDraft.Create("new", "新助手", null, "{}", "actor", "key");
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
        tool.Complete("{}", DateTime.UtcNow);
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
        await Assert.ThrowsAsync<SmartAssistant.Platform.BizResultException>(() => db.CommitAsync());
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
    /// 建立仅执行初始及审计迁移的升级前数据库，并登记实际脚本校验值。
    /// </summary>
    private static void ApplyThroughAuditMigration(ISqlSugarClient db)
    {
        db.Ado.ExecuteCommand("CREATE TABLE assistant_schema_migration (version TEXT PRIMARY KEY, sha256 TEXT NOT NULL, applied_at TEXT NOT NULL)");
        foreach (var version in new[] { "001_initial.sql", "002_remove_audit_fields.sql" })
        {
            var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Migrations", version));
            foreach (var statement in System.Text.Encoding.UTF8.GetString(bytes).Split(';', StringSplitOptions.RemoveEmptyEntries))
                if (!string.IsNullOrWhiteSpace(statement)) db.Ado.ExecuteCommand(statement);
            db.Ado.ExecuteCommand("INSERT INTO assistant_schema_migration VALUES (@version, @hash, '2026-11-01')",
                new SugarParameter("@version", version),
                new SugarParameter("@hash", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant()));
        }
    }

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
    /// 根据数据库结构填充必填值，允许同一测试辅助方法同时创建升级前后的数据。
    /// </summary>
    private static void Insert(ISqlSugarClient db, string table, string id, params (string Name, object Value)[] overrides)
    {
        var values = new Dictionary<string, object>
        {
            ["id"] = id,
            ["tenant_id"] = "tenant",
            ["org_id"] = "org",
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
