#nullable enable
using System.Text.Json;
using SmartAssistant.Models.Runtime;

namespace SmartAssistant.Models.Publication;

/// <summary>
/// 由服务端解析的公开助手权限与配置，确定当前可访问的发布范围。
/// </summary>
public sealed record AssistantPublicAuthority(
    string AssistantKey,
    AssistantScopeSnapshot Scope,
    string Name,
    string? Description,
    JsonElement Configuration,
    long VersionNo,
    string SnapshotHash);
