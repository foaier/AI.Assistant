using SmartAssistant.Extensions.DAL;
using SmartAssistant.Modules.Domain;
using SmartAssistant.Modules.Infrastructure;
using Microsoft.Extensions.Configuration;
using SmartAssistant.Persistence;
using Xunit;

namespace SmartAssistant.Tests;

public sealed class StandalonePersistenceTests
{
    [Fact]
    public void Existing_database_accepts_original_initial_migration_checksum_after_comments_are_added()
    {
        var config = NewConfiguration();
        var directory = LocalDatabase.DataDirectory(config);
        const string originalChecksum = "7a539c15b302e6f14dfd829dea61295de38de377bd687ccabd5d0cc8f4eeb148";
        LocalDatabase.Migrate(directory);

        using (var database = LocalDatabase.Open(directory))
        {
            database.Ado.ExecuteCommand(
                "UPDATE assistant_schema_migration SET sha256 = @checksum WHERE version = @version",
                new SqlSugar.SugarParameter("@checksum", originalChecksum),
                new SqlSugar.SugarParameter("@version", "001_initial.sql"));
        }

        LocalDatabase.Migrate(directory);

        using var reopened = LocalDatabase.Open(directory);
        Assert.Equal(originalChecksum, reopened.Ado.GetString(
            "SELECT sha256 FROM assistant_schema_migration WHERE version = @version",
            new SqlSugar.SugarParameter("@version", "001_initial.sql")));
        Assert.Equal(1, reopened.Ado.GetInt("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'assistant_draft'"));
    }

    [Fact]
    public async Task Aggregate_and_outbox_rollback_together_when_any_enqueued_command_fails()
    {
        var config = NewConfiguration();
        LocalDatabase.Migrate(LocalDatabase.DataDirectory(config));
        using var db = new LocalDbContext(config);
        var repository = new AssistantDraftRepository(db);
        var publisher = new LocalEventPublisher(db);
        var draft = AssistantDraft.Create("rollback-draft", "tenant", "org", "事务助手", null, "{}", "actor", null, DateTime.UtcNow);
        await repository.AddAsync(draft, CancellationToken.None);
        await publisher.PublishAsync(new AssistantDraftSavedEvent(draft.Id, "tenant", "org", 1, "actor"));
        db.AddCommand(_ => Task.FromException<int>(new InvalidOperationException("forced-last-command-failure")));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => db.CommitAsync());
        Assert.Equal("forced-last-command-failure", error.Message);
        Assert.Null(await repository.GetAsync("tenant", "org", draft.Id, CancellationToken.None));
        Assert.Equal(0, await db.GetSugarScope(ConnectionType.Master).Queryable<LocalOutboxEvent>().CountAsync());

        await repository.AddAsync(draft, CancellationToken.None);
        await publisher.PublishAsync(new AssistantDraftSavedEvent(draft.Id, "tenant", "org", 1, "actor"));
        await db.CommitAsync();
        Assert.NotNull(await repository.GetAsync("tenant", "org", draft.Id, CancellationToken.None));
        var message = Assert.Single(await db.GetSugarScope(ConnectionType.Master).Queryable<LocalOutboxEvent>().ToListAsync());
        Assert.Equal("assistant-draft-saved:rollback-draft:1", message.Id);
    }

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

    private static IConfiguration NewConfiguration()
        => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Assistant:DataDirectory"] = Path.Combine(AppContext.BaseDirectory, "TestResults", "test-data", Guid.NewGuid().ToString("N"))
        }).Build();
}
