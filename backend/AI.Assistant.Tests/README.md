# AI.Assistant tests

The 22 files listed in `source-manifest.json` were copied from the original focused
Assistant tests without changing assertions. The manifest records their original
SHA-256 hashes. `AssistantPolicyTestDatabase.cs` contains only the shared credential
fixture used by provider unit tests; it does not connect to a database.

Original tests that require the Glevent MySQL schema, Redis, Qdrant, activity or
schedule modules, repository paths, or the original host middleware are outside
this standalone test project. Seven new SQLite/standalone integration tests cover
the new host boundary independently:

- Direct management and asset upload without login/cookies, retired auth routes,
  no generated password file, and no injected user identity or claims.
- Assistant creation idempotency, stale revision rejection, restart persistence,
  and physical deletion and cascade behavior.
- Model/policy configuration, published snapshots, draft isolation, and revocation.
- Knowledge questions and local document uploads.
- Atomic aggregate/outbox rollback and successful retry after a queued failure.
- Persistent local cosine search, collection isolation, allowlists, and dimensions.
- Public sessions/conversations, model execution, real SSE events, persisted answer
  and turn completion, analytics, and session loss of access after revocation.

The tests create unique SQLite directories below their output `TestResults/`
folder and make no external model calls. The public chat test replaces only the
upstream HTTP handler with an OpenAI SSE protocol fixture and a fixed test token.
The real model adapter, credential revision resolver, orchestrator, database,
controller and SSE transport execute normally. The two tool boundary cases also
prove publication refuses original-app activity/schedule tools.

Nine configuration cases verify local JSON/appsettings overrides, local
`config://` / `appsettings://` / legacy `apollo://` credential resolution, explicit
environment references, missing-secret failures, optional references, and
cancellation. The full public chat case resolves its fixture key through
`config://` and the real host configuration resolver.

Run from the project root:

```powershell
dotnet test backend/AI.Assistant.Tests/AI.Assistant.Tests.csproj
```
