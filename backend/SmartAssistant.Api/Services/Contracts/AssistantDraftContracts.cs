#nullable enable
using SmartAssistant.Models.Analytics;
using SmartAssistant.Models.Assistants;
using SmartAssistant.Models.Knowledge;
using SmartAssistant.Models.Publication;
using SmartAssistant.Models.Runtime;

namespace SmartAssistant.Services.Contracts;

/// <summary>
/// 定义知识库、问答、文档、分块配置和索引的应用操作。
/// </summary>
public interface IAssistantKnowledgeService
{
    /// <summary>
    /// 列出知识库及其问答、文档数量。
    /// </summary>
    Task<IReadOnlyList<AssistantKnowledgeBaseDto>> ListKnowledgeBasesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 校验名称与范围后创建知识库。
    /// </summary>
    Task<AssistantKnowledgeBaseDto> CreateKnowledgeBaseAsync(
        CreateAssistantKnowledgeBaseCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查草稿和有效发布的知识库引用后执行删除，避免破坏已绑定的助手。
    /// </summary>
    Task DeleteKnowledgeBaseAsync(
        string knowledgeBaseId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 读取知识库当前分块策略与是否存在待重处理文档的信息。
    /// </summary>
    Task<AssistantKnowledgeChunkingPolicyDto> GetChunkingPolicyAsync(
        string knowledgeBaseId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 校验并保存分块参数，按请求决定是否为现有文档启动重新处理。
    /// </summary>
    Task<AssistantKnowledgeChunkingPolicyDto> SaveChunkingPolicyAsync(
        string knowledgeBaseId,
        SaveAssistantKnowledgeChunkingPolicyCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 读取目标与生效嵌入配置、文档完成进度和可选模型。
    /// </summary>
    Task<AssistantKnowledgeEmbeddingProfileDto> GetEmbeddingProfileAsync(
        string knowledgeBaseId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 校验嵌入模型与预期修订号后保存目标配置，并维护相应索引代次。
    /// </summary>
    Task<AssistantKnowledgeEmbeddingProfileDto> SaveEmbeddingProfileAsync(
        string knowledgeBaseId,
        SaveAssistantKnowledgeEmbeddingProfileCommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按当前分块策略预览文档切分结果。
    /// </summary>
    Task<AssistantKnowledgeChunkPreviewDto> PreviewChunkingAsync(
        string knowledgeBaseId, string documentId,
        SaveAssistantKnowledgeChunkingPolicyCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 读取知识库固定问答列表。
    /// </summary>
    Task<IReadOnlyList<AssistantKnowledgeQnaDto>> ListQnaAsync(
        string knowledgeBaseId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 标准化问题并保存问答内容、排序、启用状态和推荐设置。
    /// </summary>
    Task<AssistantKnowledgeQnaDto> SaveQnaAsync(
        string knowledgeBaseId,
        SaveAssistantKnowledgeQnaCommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// 在所属知识库范围内删除指定固定问答。
    /// </summary>
    Task DeleteQnaAsync(
        string knowledgeBaseId, string qnaId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 按固定问答匹配规则验证问题是否命中，返回直接回答结果。
    /// </summary>
    Task<AssistantKnowledgeQnaTestResult> TestQnaAsync(
        string knowledgeBaseId, string qnaId,
        TestAssistantKnowledgeQnaCommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// 列出知识库文档及其处理状态。
    /// </summary>
    Task<IReadOnlyList<AssistantKnowledgeDocumentDto>> ListDocumentsAsync(
        string knowledgeBaseId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 将文档保存为私有资产，并创建知识文档记录。
    /// </summary>
    Task<AssistantKnowledgeDocumentDto> UploadDocumentAsync(
        string knowledgeBaseId,
        AssistantKnowledgeDocumentUpload file, CancellationToken cancellationToken = default);

    /// <summary>
    /// 为指定文档执行解析、分块和向量索引流程，保存版本及任务状态。
    /// </summary>
    Task<AssistantKnowledgeDocumentProcessResult> ProcessDocumentAsync(
        string knowledgeBaseId, string documentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 读取文档原文、结构和片段，供管理端预览。
    /// </summary>
    Task<AssistantKnowledgeDocumentPreviewDto> GetDocumentPreviewAsync(
        string knowledgeBaseId, string documentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 在知识库生效的索引代次上执行检索测试，返回命中片段及来源。
    /// </summary>
    Task<AssistantKnowledgeRetrievalTestResult> TestRetrievalAsync(
        string knowledgeBaseId,
        TestAssistantKnowledgeRetrievalCommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// 读取知识库索引任务及进度、失败信息。
    /// </summary>
    Task<IReadOnlyList<AssistantKnowledgeIndexJobDto>> ListIndexJobsAsync(
        string knowledgeBaseId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 定义管理端查询会话明细和助手使用统计的应用契约。
/// </summary>
public interface IAssistantAnalyticsService
{
    /// <summary>
    /// 在指定助手范围内读取会话消息，供管理端查看会话内容。
    /// </summary>
    Task<AssistantMessagesPage> GetMessagesAsync(string assistantId,
        string conversationId, int limit, string? cursor, CancellationToken cancellationToken = default);

    /// <summary>
    /// 分页读取助手的会话活动信息。
    /// </summary>
    Task<AssistantConversationsPage> ListConversationsAsync(
        string assistantId, int limit, int page,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 汇总助手的会话数量、轮次结果和回答来源等统计数据。
    /// </summary>
    Task<AssistantAnalyticsSummaryDto> GetSummaryAsync(
        string assistantId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 定义助手发布、撤回及当前发布状态查询的应用契约。
/// </summary>
public interface IAssistantPublicationService
{
    /// <summary>
    /// 读取助手当前的发布状态、指针修订和公开访问标识。
    /// </summary>
    Task<AssistantPublicationState> GetStateAsync(string assistantId, CancellationToken cancellationToken = default);
    /// <summary>
    /// 校验草稿与指针修订，冻结执行配置并在同一事务中保存发布版本、指针和领域事件。
    /// </summary>
    Task<AssistantPublicationResult> PublishAsync(
        string assistantId,
        PublishAssistantCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 校验预期发布指针修订，撤回当前版本并记录撤回事件。
    /// </summary>
    Task<AssistantPublicationResult> RevokeAsync(
        string assistantId,
        RevokeAssistantPublicationCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 读取当前指针指向的有效发布版本；未发布或已撤回时返回空值。
    /// </summary>
    Task<AssistantPublishedVersionDto?> GetCurrentAsync(
        string assistantId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 按公开标识解析有效发布权限；不可用时返回空结果。
/// </summary>
public interface IAssistantPublicAuthorityResolver
{
    /// <summary>
    /// 按公开助手标识解析有效的发布快照及运行授权范围；不可用时返回空值。
    /// </summary>
    Task<AssistantPublicAuthority?> ResolveAsync(
        string assistantKey,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 定义助手草稿的增删改查及管理端数据转换契约。
/// </summary>
public interface IAssistantDraftService
{
    /// <summary>
    /// 读取助手列表。
    /// </summary>
    Task<IReadOnlyList<AssistantListItemDto>> ListAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 创建草稿并转换为管理端使用的助手详情。
    /// </summary>
    Task<AssistantDto> CreateAssistantAsync(
        CreateAssistantDraftCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 读取草稿并将配置转换为管理端的 JSON 对象。
    /// </summary>
    Task<AssistantDto> GetAssistantAsync(
        string assistantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 保存草稿并返回更新后的管理端助手详情。
    /// </summary>
    Task<AssistantDto> SaveAssistantDraftAsync(
        string assistantId,
        SaveAssistantDraftCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除草稿前检查是否仍有有效发布，已发布助手必须先撤回。
    /// </summary>
    Task DeleteAssistantAsync(
        string assistantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 校验配置与幂等键后创建助手草稿，重复的相同请求复用已有结果。
    /// </summary>
    Task<AssistantDraftDto> CreateAsync(
        CreateAssistantDraftCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 读取指定助手草稿；资源不存在时返回相应业务错误。
    /// </summary>
    Task<AssistantDraftDto> GetAsync(
        string assistantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 校验配置绑定与预期草稿修订号后保存变更，并记录草稿保存事件。
    /// </summary>
    Task<AssistantDraftDto> SaveAsync(
        string assistantId,
        SaveAssistantDraftCommand command,
        CancellationToken cancellationToken = default);
}
