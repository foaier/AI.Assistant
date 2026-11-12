using System.Reflection;
using Grpc.Core;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using SmartAssistant.Modules.Infrastructure;
using Xunit;

namespace SmartAssistant.Tests;

/// <summary>
/// 验证移除旧范围摘要后，远程向量适配器仍复用正确的已有集合。
/// </summary>
public sealed class AssistantVectorCollectionUpgradeTests
{
    /// <summary>
    /// 验证查询、写入和回读均使用同一个旧集合，已有新集合时优先使用新集合。
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Remote_vector_operations_preserve_existing_collections(bool hasCurrentCollection)
    {
        var name = AssistantKnowledgeIndexNaming.Collection("knowledge", "profile");
        var legacyName = "assistant_kb_0123456789ab_abcdef012345_" + name[13..];
        var client = DispatchProxy.Create<IQdrantClient, CollectionProxy>();
        var proxy = (CollectionProxy)(object)client;
        proxy.Names = hasCurrentCollection ? [legacyName, name] : [legacyName];
        var expectedName = hasCurrentCollection ? name : legacyName;
        var store = new QdrantAssistantVectorIndexStore(client);

        var hits = await store.SearchAsync(name, new float[] { 1, 0 }, 5, CancellationToken.None);
        Assert.Equal("chunk", Assert.Single(hits).ChunkId);
        AssistantVectorChunk[] chunks = [new("chunk", "hash", "document", 1, 0, "内容", [1, 0])];
        await store.UpsertAsync(name, 2, chunks, CancellationToken.None);
        await store.ReadBackAsync(name, chunks, CancellationToken.None);

        Assert.Equal(new[] { "QueryAsync", "CreateCollectionAsync", "UpsertAsync", "RetrieveAsync" },
            proxy.Operations.Select(operation => operation.Method));
        Assert.All(proxy.Operations, operation => Assert.Equal(expectedName, operation.Collection));
        Assert.Equal(1, proxy.ListCalls);
    }

    /// <summary>
    /// 验证多个旧集合映射到同一新名称时拒绝访问，避免任意选取其他集合的数据。
    /// </summary>
    [Fact]
    public async Task Ambiguous_legacy_collections_are_rejected_before_querying_vectors()
    {
        var name = AssistantKnowledgeIndexNaming.Collection("knowledge", "profile");
        var client = DispatchProxy.Create<IQdrantClient, CollectionProxy>();
        var proxy = (CollectionProxy)(object)client;
        proxy.Names =
        [
            "assistant_kb_0123456789ab_abcdef012345_" + name[13..],
            "assistant_kb_111111111111_222222222222_" + name[13..]
        ];

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new QdrantAssistantVectorIndexStore(client).SearchAsync(name, new float[] { 1, 0 }, 5, CancellationToken.None));
        Assert.Equal("assistant_vector_collection_name_conflict", error.Message);
        Assert.Empty(proxy.Operations);
    }

    /// <summary>
    /// 仅替代远程传输，记录向量操作使用的集合并返回受控协议数据。
    /// </summary>
    public class CollectionProxy : DispatchProxy
    {
        /// <summary>
        /// 指定远程服务已经存在的集合名称。
        /// </summary>
        public string[] Names { get; set; } = [];

        /// <summary>
        /// 记录实际向量操作及其目标集合。
        /// </summary>
        public List<(string Method, string Collection)> Operations { get; } = [];

        /// <summary>
        /// 记录集合列表请求数，用于验证名称解析结果被复用。
        /// </summary>
        public int ListCalls { get; private set; }

        /// <summary>
        /// 模拟集合发现、已存在响应和向量查询，未预期的调用立即失败。
        /// </summary>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var method = targetMethod ?? throw new InvalidOperationException();
            if (method.Name == "ListCollectionsAsync")
            {
                ListCalls++;
                return Completed(method, Names);
            }

            Operations.Add((method.Name, (string)args![0]!));
            if (method.Name == "CreateCollectionAsync")
                throw new RpcException(new Status(StatusCode.AlreadyExists, "existing collection"));
            if (method.Name == "QueryAsync")
            {
                var point = new ScoredPoint { Score = 1 };
                point.Payload["chunk_id"] = new Value { StringValue = "chunk" };
                return Completed(method, new[] { point });
            }
            if (method.Name == "RetrieveAsync") return Completed(method, Array.Empty<RetrievedPoint>());
            if (method.Name == "UpsertAsync") return Completed(method);
            throw new NotSupportedException(method.Name);
        }

        /// <summary>
        /// 按接口声明构造已完成任务，兼容客户端各操作的协议响应类型。
        /// </summary>
        private static object Completed(MethodInfo method, object? value = null)
        {
            var resultType = method.ReturnType.GetGenericArguments()[0];
            return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(resultType)
                .Invoke(null, [value ?? Activator.CreateInstance(resultType)])!;
        }
    }
}
