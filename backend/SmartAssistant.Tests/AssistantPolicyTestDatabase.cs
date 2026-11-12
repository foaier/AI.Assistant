using SmartAssistant.Platform.Security;

namespace SmartAssistant.Tests;

// The standalone provider unit tests share only this credential fixture. They do
// not initialize, modify or connect to the original application's MySQL database.
internal static class AssistantPolicyTestDatabase
{
    internal sealed class TestSecrets : ICredentialMaterialResolver
    {
        public int Calls { get; private set; }
        public Task<string> ResolveRequiredAsync(string reference, string version, CancellationToken ct = default)
        { Calls++; return Task.FromResult("test-key-material"); }
        public Task<string?> ResolveOptionalAsync(string? reference, string? version, CancellationToken ct = default)
            => Task.FromResult<string?>(null);
    }
}
