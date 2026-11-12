#nullable enable
namespace SmartAssistant.Models.Runtime;

/// <summary>
/// 匿名会话创建结果，同时返回会话快照与供客户端后续请求使用的令牌。
/// </summary>
public sealed record AssistantSessionCreated(
    AnonymousAssistantSessionSnapshot Session,
    string BearerToken);
