using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using SqlSugar;
using AI.Assistant.Platform.Events;
using AI.Assistant.Services.Domain.Entities;

namespace AI.Assistant.Extensions.DAL
{
    /// <summary>
    /// 仓储共用的数据库与事务边界，将写入命令排队后统一提交。
    /// </summary>
    public interface IDbContext
    {
        /// <summary>
        /// 取得当前事务边界使用的数据库客户端。
        /// </summary>
        ISqlSugarClient GetSugarScope();
        /// <summary>
        /// 将数据库写入命令加入待提交队列；调用本方法不会立即执行该写入。
        /// </summary>
        void AddCommand(Func<ISqlSugarClient, Task<int>> command);
        /// <summary>
        /// 依次执行排队命令并提交事务；失败或取消时回滚本批写入。
        /// </summary>
        Task CommitAsync(CancellationToken cancellationToken = default);
    }
}

namespace AI.Assistant.Persistence
{
    /// <summary>
    /// 管理本地 SQLite 连接及当前开发结构的初始化和校验。
    /// </summary>
    public static class LocalDatabase
    {
        /// <summary>
        /// 串行化进程内数据库初始化，避免并发建表。
        /// </summary>
        private static readonly object MigrationLock = new();
        /// <summary>
        /// 登记当前初始化脚本及校验值。
        /// </summary>
        private static readonly (string Version, string Sha256)[] Migrations =
        [
            ("001_initial.sql", "353b344c285e184536a118871ffbe8cac9a286c249048cb1665e2b663d65e925")
        ];

        /// <summary>
        /// 从配置解析本地数据目录，并返回规范化的绝对路径。
        /// </summary>
        public static string DataDirectory(IConfiguration configuration)
        {
            var directory = configuration["Assistant:DataDirectory"];
            return Path.GetFullPath(string.IsNullOrWhiteSpace(directory)
                ? Path.Combine(AppContext.BaseDirectory, "data") : directory);
        }

        /// <summary>
        /// 打开本地 SQLite 数据库并启用外键与锁等待设置。
        /// </summary>
        public static SqlSugarClient Open(string dataDirectory)
        {
            var directory = Path.GetFullPath(dataDirectory);
            Directory.CreateDirectory(directory);
            var databaseFile = Path.Combine(directory, "assistant.db").Replace("\"", "\"\"");
            var database = new SqlSugarClient(new ConnectionConfig
            {
                DbType = DbType.Sqlite,
                ConnectionString = $"Data Source=\"{databaseFile}\"",
                InitKeyType = InitKeyType.Attribute,
                IsAutoCloseConnection = false
            });
            database.Ado.Connection.Open();
            database.Ado.ExecuteCommand("PRAGMA foreign_keys = ON;");
            database.Ado.ExecuteCommand("PRAGMA busy_timeout = 30000;");
            return database;
        }

        /// <summary>
        /// 事务化创建当前数据库结构，重复启动时核对初始化脚本校验和。
        /// </summary>
        public static void Migrate(string dataDirectory)
        {
            lock (MigrationLock)
            {
                using var database = Open(dataDirectory);
                database.Ado.ExecuteCommand("PRAGMA journal_mode = WAL;");
                database.Ado.ExecuteCommand("""
                    CREATE TABLE IF NOT EXISTS assistant_schema_migration (
                        version TEXT NOT NULL PRIMARY KEY,
                        sha256 TEXT NOT NULL,
                        applied_at TEXT NOT NULL
                    );
                    """);
                foreach (var (version, expectedChecksum) in Migrations)
                {
                    var file = Path.Combine(AppContext.BaseDirectory, "Migrations", version);
                    var bytes = File.ReadAllBytes(file);
                    var checksum = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                    if (!string.Equals(checksum, expectedChecksum, StringComparison.Ordinal))
                        throw new InvalidOperationException($"Migration checksum mismatch: {version}");
                    var applied = database.Ado.GetString(
                        "SELECT sha256 FROM assistant_schema_migration WHERE version = @version",
                        new SugarParameter("@version", version));
                    if (!string.IsNullOrEmpty(applied))
                    {
                        if (!string.Equals(applied, checksum, StringComparison.Ordinal))
                            throw new InvalidOperationException($"Applied migration checksum mismatch: {version}. Recreate the development database.");
                        continue;
                    }

                    database.Ado.BeginTran();
                    try
                    {
                        /*
                         * 迁移文件限定为普通 DDL，不包含触发器或字符串内的分号，因此可按语句分隔执行。
                         */
                        var sql = System.Text.RegularExpressions.Regex.Replace(
                            System.Text.Encoding.UTF8.GetString(bytes), @"(?m)^\s*--.*$", "");
                        foreach (var statement in sql.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                            database.Ado.ExecuteCommand(statement);
                        database.Ado.ExecuteCommand(
                            "INSERT INTO assistant_schema_migration(version, sha256, applied_at) VALUES (@version, @sha256, @appliedAt)",
                            new SugarParameter("@version", version),
                            new SugarParameter("@sha256", checksum),
                            new SugarParameter("@appliedAt", DateTime.Now.ToString("O")));
                        database.Ado.CommitTran();
                    }
                    catch
                    {
                        database.Ado.RollbackTran();
                        throw;
                    }
                }
            }
        }
    }

    /// <summary>
    /// 在同一 SQLite 事务中执行排队的业务和事件写入命令。
    /// 任一命令失败时回滚本批写入，避免聚合与 Outbox 状态不一致。
    /// </summary>
    public sealed class LocalDbContext : AI.Assistant.Extensions.DAL.IDbContext, IDisposable
    {
        /// <summary>
        /// 保存客户端依赖，供本地数据库上下文执行相关操作。
        /// </summary>
        private readonly SqlSugarClient _database;
        /// <summary>
        /// 保存本地数据库上下文在处理命令时所需的数据或状态。
        /// </summary>
        private readonly List<Func<ISqlSugarClient, Task<int>>> _commands = [];
        /// <summary>
        /// 串行化本地数据库上下文中的临界写入，避免共享状态并发损坏。
        /// </summary>
        private readonly SemaphoreSlim _commitGate = new(1, 1);

        /// <summary>
        /// 接收并保存本地数据库上下文执行所需的依赖，建立后续操作使用的运行状态。
        /// </summary>
        public LocalDbContext(IConfiguration configuration)
            => _database = LocalDatabase.Open(LocalDatabase.DataDirectory(configuration));

        /// <summary>
        /// 取得当前事务边界使用的数据库客户端。
        /// </summary>
        public ISqlSugarClient GetSugarScope() => _database;

        /// <summary>
        /// 将数据库写入命令加入待提交队列；调用本方法不会立即执行该写入。
        /// </summary>
        public void AddCommand(Func<ISqlSugarClient, Task<int>> command)
        {
            ArgumentNullException.ThrowIfNull(command);
            _commands.Add(command);
        }

        /// <summary>
        /// 依次执行排队命令并提交事务；失败或取消时回滚本批写入。
        /// </summary>
        public async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            await _commitGate.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_commands.Count == 0) return;
                var commands = _commands.ToArray();
                _commands.Clear();
                await _database.Ado.BeginTranAsync();
                try
                {
                    foreach (var command in commands)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await command(_database);
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    await _database.Ado.CommitTranAsync();
                }
                catch
                {
                    await _database.Ado.RollbackTranAsync();
                    throw;
                }
            }
            finally
            {
                _commitGate.Release();
            }
        }

        /// <summary>
        /// 释放实例持有的文件、连接或同步资源，避免资源泄漏。
        /// </summary>
        public void Dispose()
        {
            _commands.Clear();
            _database.Dispose();
            _commitGate.Dispose();
        }
    }

    /// <summary>
    /// 将领域事件加入本地 Outbox 写入队列，与业务变更共用提交事务。
    /// </summary>
    public sealed class LocalEventPublisher(LocalDbContext database) : IDomainEventPublisher
    {
        /// <summary>
        /// 序列化领域事件并将其 Outbox 写入加入当前事务队列。
        /// </summary>
        public Task PublishAsync<T>(T @event) where T : DomainEvent
        {
            ArgumentNullException.ThrowIfNull(@event);
            var row = new LocalOutboxEventEntity
            {
                Id = @event.Id,
                EventType = @event.GetType().FullName ?? @event.GetType().Name,
                PayloadJson = JsonSerializer.Serialize(@event, @event.GetType())
            };
            /*
             * 事件与聚合写入进入同一事务队列，提交时一起落库，失败时一起回滚。
             */
            database.AddCommand(client => client.Insertable(row).ExecuteCommandAsync());
            return Task.CompletedTask;
        }
    }
}
