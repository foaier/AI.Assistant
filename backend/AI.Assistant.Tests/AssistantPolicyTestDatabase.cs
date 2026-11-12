using AI.Assistant.Platform.Security;

namespace AI.Assistant.Tests;

// The standalone provider unit tests share only this credential fixture. They do
// not initialize, modify or connect to the original application's MySQL database.
/// <summary>
/// 测试辅助类型，为相关测试提供可控数据或依赖替身。
/// </summary>
internal static class AssistantPolicyTestDatabase
{
    /// <summary>
    /// 测试辅助类型，为相关测试提供可控数据或依赖替身。
    /// </summary>
    internal sealed class TestSecrets : ICredentialMaterialResolver
    {
        /// <summary>
        /// 表示测试密钥的调用信息。
        /// </summary>
        public int Calls { get; private set; }
        /// <summary>
        /// 验证解析相关行为、预期结果及错误边界。
        /// </summary>
        public Task<string> ResolveRequiredAsync(string reference, string version, CancellationToken ct = default)
        { Calls++; return Task.FromResult("test-key-material"); }
        /// <summary>
        /// 验证解析相关行为、预期结果及错误边界。
        /// </summary>
        public Task<string?> ResolveOptionalAsync(string? reference, string? version, CancellationToken ct = default)
            => Task.FromResult<string?>(null);
    }
}
