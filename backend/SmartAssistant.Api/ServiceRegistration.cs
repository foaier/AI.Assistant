using SmartAssistant.Extensions.DAL;
using SmartAssistant.Services.Application;
using SmartAssistant.Services.Infrastructure;
using SmartAssistant.Services.Infrastructure.AI;
using SmartAssistant.Services.Infrastructure.AssistantTransport;
using SmartAssistant.Services.Contracts;
using SmartAssistant.Platform.Assets;
using SmartAssistant.Platform.Events;
using SmartAssistant.Platform.Security;
using Qdrant.Client;
using SmartAssistant.Persistence;
using SmartAssistant.Models.AI;

namespace SmartAssistant;

/// <summary>
/// 集中注册助手业务服务、持久化适配器、模型供应商和事件传输组件。
/// </summary>
public static class ServiceRegistration
{
    /// <summary>
    /// 按配置选择本地或外部基础设施，并注册运行助手所需的依赖与生命周期。
    /// </summary>
    public static void AddAssistant(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<LocalDbContext>();
        services.AddScoped<IDbContext>(sp => sp.GetRequiredService<LocalDbContext>());
        services.AddScoped<IDomainEventPublisher, LocalEventPublisher>();
        services.AddSingleton<ICredentialMaterialResolver, ConfigurationCredentialMaterialResolver>();
        services.AddSingleton<AssetApplicationService>();
        services.AddSingleton<AssistantProviderSecretProtector>();
        services.AddScoped<IAssistantDraftRepository, AssistantDraftRepository>();
        services.AddScoped<IAssistantPolicyRepository, AssistantPolicyRepository>();
        services.AddScoped<IAssistantKnowledgeRepository, AssistantKnowledgeRepository>();
        services.AddScoped<IAssistantKnowledgeEmbeddingProfileRepository, AssistantKnowledgeEmbeddingProfileRepository>();
        services.AddScoped<IAssistantKnowledgeDocumentRepository, AssistantKnowledgeDocumentRepository>();
        services.AddScoped<IAssistantAnalyticsRepository, AssistantAnalyticsRepository>();
        services.AddScoped<IAssistantPublicationRepository, AssistantPublicationRepository>();
        services.AddScoped<IAssistantRuntimeRepository, AssistantRuntimeRepository>();
        services.AddScoped<AssistantPreviewRepository>();
        services.AddScoped<IAssistantDraftService, AssistantDraftService>();
        services.AddScoped<IAssistantPolicyService, AssistantPolicyService>();
        services.AddScoped<IAssistantKnowledgeService, AssistantKnowledgeService>();
        services.AddScoped<IAssistantAnalyticsService, AssistantAnalyticsService>();
        services.AddScoped<IAssistantPublicationService, AssistantPublicationService>();
        services.AddScoped<IAssistantPublicAuthorityResolver, AssistantPublicAuthorityResolver>();
        services.AddScoped<AssistantPreviewService>();
        services.AddScoped<AssistantExecutionSnapshotService>();
        services.AddScoped<AssistantWebSearchConfigurationService>();
        services.AddScoped<IAssistantKnowledgeRuntimeRetriever, AssistantKnowledgeRuntimeRetriever>();
        services.AddSingleton<IAssistantDocumentParser, AssistantDocumentParser>();
        services.AddScoped<IAssistantEmbeddingProvider, OpenAiCompatibleEmbeddingProvider>();
        services.AddSingleton<IAssistantVectorIndexStore>(sp =>
        {
            var endpoint = configuration["Assistant:Knowledge:Qdrant:Endpoint"];
            return Uri.TryCreate(endpoint, UriKind.Absolute, out var address)
                ? new QdrantAssistantVectorIndexStore(new QdrantClient(address, configuration["Assistant:Knowledge:Qdrant:ApiKey"]))
                : new LocalVectorIndex(configuration);
        });
        services.AddScoped<AssistantProviderCredentialResolver>();
        services.AddScoped<IAssistantProviderCredentialResolver>(sp => sp.GetRequiredService<AssistantProviderCredentialResolver>());
        foreach (var name in new[] { "AssistantModelConnection", "assistant-volc-agent", "assistant-provider" })
            services.AddHttpClient(name, client => client.Timeout = Timeout.InfiniteTimeSpan)
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddScoped<IAssistantWebSearchClient, VolcengineAssistantWebSearchClient>();
        services.AddScoped<IChatModelProvider>(sp => new OpenAiCompatibleChatModelProvider(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("assistant-provider"),
            sp.GetRequiredService<IAssistantProviderCredentialResolver>(),
            new OpenAiCompatibleProviderOptions(
                TimeSpan.FromSeconds(Math.Clamp(configuration.GetValue("Assistant:Provider:TimeoutSeconds", 90), 1, 600)),
                configuration.GetValue("Assistant:Provider:MaxResponseBytes", 4 * 1024 * 1024))));
        services.AddSingleton<IRunEventBroker>(sp =>
        {
            var redisConnection = configuration["Assistant:Redis:ConnectionString"];
            return !string.IsNullOrWhiteSpace(redisConnection)
                ? new RedisAssistantRunEventBroker(StackExchange.Redis.ConnectionMultiplexer.Connect(redisConnection))
                : new InMemoryAssistantRunEventBroker();
        });
        services.AddScoped<IStreamTicketService>(sp => new SignedAssistantStreamTicketService(AssistantStreamTicketSigningKey.Resolve(configuration)));
        services.AddScoped<AssistantRunEventTransportSerializer>();
        services.AddScoped<AssistantSseStreamAdapter>();
        services.AddScoped<AssistantWebSocketStreamAdapter>();
        services.AddScoped<AssistantRunOrchestrator>();
        services.AddScoped<IAssistantRuntimeOrchestrator>(sp => sp.GetRequiredService<AssistantRunOrchestrator>());
        services.AddScoped<IAssistantPublicRuntimeService, AssistantPublicRuntimeService>();
        services.AddScoped<IAssistantToolRegistry, AssistantToolRegistry>();
        services.AddScoped<IAssistantToolHandler, AssistantVolcengineWebSearchToolHandler>();
    }
}
