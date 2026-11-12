using System.Reflection;
using Grpc.Core;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using AI.Assistant.Services.Infrastructure;
using Xunit;
using AI.Assistant.Models.Knowledge;

namespace AI.Assistant.Tests;

/// <summary>
/// 验证远程向量适配器的集合隔离及查询、写入和回读协议。
/// </summary>
public sealed class AssistantVectorIndexStoreTests
{
    /// <summary>
    /// 验证查询、写入和回读均使用调用方指定的集合名称。
    /// </summary>
    [Fact]
    public async Task Remote_vector_operations_use_the_requested_collection()
    {
        var name = AssistantKnowledgeIndexNaming.Collection("knowledge", "profile");
        var client = DispatchProxy.Create<IQdrantClient, CollectionProxy>();
        var proxy = (CollectionProxy)(object)client;
        var store = new QdrantAssistantVectorIndexStore(client);

        var hits = await store.SearchAsync(name, new float[] { 1, 0 }, 5, CancellationToken.None);
        Assert.Equal("chunk", Assert.Single(hits).ChunkId);
        AssistantVectorChunk[] chunks = [new("chunk", "hash", "document", 1, 0, "内容", [1, 0])];
        await store.UpsertAsync(name, 2, chunks, CancellationToken.None);
        await store.ReadBackAsync(name, chunks, CancellationToken.None);

        Assert.Equal(new[] { "QueryAsync", "CreateCollectionAsync", "UpsertAsync", "RetrieveAsync" },
            proxy.Operations.Select(operation => operation.Method));
        Assert.All(proxy.Operations, operation => Assert.Equal(name, operation.Collection));
    }

    /// <summary>
    /// 仅替代远程传输，记录向量操作使用的集合并返回受控协议数据。
    /// </summary>
    public class CollectionProxy : DispatchProxy
    {
        /// <summary>
        /// 记录实际向量操作及其目标集合。
        /// </summary>
        public List<(string Method, string Collection)> Operations { get; } = [];

        /// <summary>
        /// 模拟集合已存在响应和向量查询，未预期的调用立即失败。
        /// </summary>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var method = targetMethod ?? throw new InvalidOperationException();
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
