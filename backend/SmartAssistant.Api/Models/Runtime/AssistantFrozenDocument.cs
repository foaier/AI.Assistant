#nullable enable
namespace SmartAssistant.Models.Runtime;

/// <summary>
/// 冻结到执行快照中的知识文档版本，避免运行期间读取未发布的新版本。
/// </summary>
public sealed record AssistantFrozenDocument(
    string Id,
    string VersionId,
    int VersionNo,
    string Name);
