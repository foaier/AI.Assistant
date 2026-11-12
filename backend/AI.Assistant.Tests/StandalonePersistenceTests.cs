using AI.Assistant.Extensions.DAL;
using AI.Assistant.Services.Domain;
using AI.Assistant.Services.Infrastructure;
using Microsoft.Extensions.Configuration;
using AI.Assistant.Persistence;
using Xunit;
using AI.Assistant.Services.Domain.Entities;
using AI.Assistant.Models.Knowledge;

namespace AI.Assistant.Tests;

/// <summary>
/// 验证独立项目持久化行为和边界条件的自动化测试集合。
/// </summary>
public sealed class StandalonePersistenceTests
{
    /// <summary>
    /// 验证初始化记录与当前脚本校验值不一致时拒绝启动，不接收历史校验值。
    /// </summary>
    [Fact]
    public void Initialization_rejects_a_database_with_a_different_schema_checksum()
    {
        var config = NewConfiguration();
        var directory = LocalDatabase.DataDirectory(config);
        var mismatchedChecksum = new string('0', 64);
        LocalDatabase.Migrate(directory);

        using (var database = LocalDatabase.Open(directory))
        {
            database.Ado.ExecuteCommand(
                "UPDATE assistant_schema_migration SET sha256 = @checksum WHERE version = @version",
                new SqlSugar.SugarParameter("@checksum", mismatchedChecksum),
                new SqlSugar.SugarParameter("@version", "001_initial.sql"));
        }

        var error = Assert.Throws<InvalidOperationException>(() => LocalDatabase.Migrate(directory));
        Assert.Contains("Recreate the development database", error.Message);

        using var reopened = LocalDatabase.Open(directory);
        Assert.Equal(mismatchedChecksum, reopened.Ado.GetString(
            "SELECT sha256 FROM assistant_schema_migration WHERE version = @version",
            new SqlSugar.SugarParameter("@version", "001_initial.sql")));
        Assert.Equal(1, reopened.Ado.GetInt("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'assistant_draft'"));
    }

    /// <summary>
    /// 验证任意待提交命令失败时，聚合和事件发件箱一并回滚。
    /// </summary>
    [Fact]
    public async Task Aggregate_and_outbox_rollback_together_when_any_enqueued_command_fails()
    {
        var config = NewConfiguration();
        LocalDatabase.Migrate(LocalDatabase.DataDirectory(config));
        using var db = new LocalDbContext(config);
        var repository = new AssistantDraftRepository(db);
        var publisher = new LocalEventPublisher(db);
        var draft = AssistantDraft.Create("rollback-draft", "事务助手", null, "{}", null);
        await repository.AddAsync(draft, CancellationToken.None);
        await publisher.PublishAsync(new AssistantDraftSavedEvent(draft.Id, 1));
        db.AddCommand(_ => Task.FromException<int>(new InvalidOperationException("forced-last-command-failure")));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => db.CommitAsync());
        Assert.Equal("forced-last-command-failure", error.Message);
        Assert.Null(await repository.GetAsync(draft.Id, CancellationToken.None));
        Assert.Equal(0, await db.GetSugarScope().Queryable<LocalOutboxEventEntity>().CountAsync());

        await repository.AddAsync(draft, CancellationToken.None);
        await publisher.PublishAsync(new AssistantDraftSavedEvent(draft.Id, 1));
        await db.CommitAsync();
        Assert.NotNull(await repository.GetAsync(draft.Id, CancellationToken.None));
        var message = Assert.Single(await db.GetSugarScope().Queryable<LocalOutboxEventEntity>().ToListAsync());
        Assert.Equal("assistant-draft-saved:rollback-draft:1", message.Id);
    }

    /// <summary>
    /// 验证重启后本地向量仍保持余弦排序、集合隔离及允许列表约束。
    /// </summary>
    [Fact]
    public async Task Local_vectors_keep_cosine_order_collection_isolation_and_allowlist_after_restart()
    {
        var config = NewConfiguration();
        LocalDatabase.Migrate(LocalDatabase.DataDirectory(config));
        var vectors = new LocalVectorIndex(config);
        AssistantVectorChunk[] chunks =
        [
            new("similar", "hash-1", "doc", 1, 0, "a", [1, 0]),
            new("orthogonal", "hash-2", "doc", 1, 1, "b", [0, 1]),
            new("opposite", "hash-3", "doc", 1, 2, "c", [-1, 0])
        ];
        await vectors.UpsertAsync("one", 2, chunks, CancellationToken.None);
        await vectors.UpsertAsync("two", 2, [new("other-collection", "other", "other-doc", 1, 0, "x", [1, 0])], CancellationToken.None);
        var restarted = new LocalVectorIndex(config);
        var hits = await restarted.SearchAsync("one", new float[] { 1, 0 }, 5, CancellationToken.None);
        Assert.Equal(new[] { "similar", "orthogonal", "opposite" }, hits.Select(x => x.ChunkId));
        Assert.Equal(new double[] { 1, 0, -1 }, hits.Select(x => x.Score));
        var allowed = await restarted.SearchAsync("one", new float[] { 1, 0 }, 5, CancellationToken.None, ["opposite"]);
        Assert.Equal("opposite", Assert.Single(allowed).ChunkId);
        Assert.Empty(await restarted.SearchAsync("one", new float[] { 1, 0 }, 5, CancellationToken.None, []));
        Assert.Equal(3, (await restarted.ReadBackAsync("one", chunks, CancellationToken.None)).Count);
        await Assert.ThrowsAsync<InvalidOperationException>(() => restarted.SearchAsync("one", new float[] { 1, 0, 0 }, 5, CancellationToken.None));
    }

    /// <summary>
    /// 验证配置相关行为、预期结果及错误边界。
    /// </summary>
    private static IConfiguration NewConfiguration()
        => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Assistant:DataDirectory"] = Path.Combine(AppContext.BaseDirectory, "TestResults", "test-data", Guid.NewGuid().ToString("N"))
        }).Build();
}
