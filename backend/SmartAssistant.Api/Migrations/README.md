# Assistant SQLite migration

`001_initial.sql` is a frozen, self-contained schema derived from these source migrations:

- `20261103041000000_136a0011_assistant_runtime_cutover.sql`
- `20261103040800000_136a0009_assistant_preview.sql`
- `20261103041100000_136a0012_assistant_knowledge_embedding_profile.sql`

The conversion retains the twenty Assistant business tables, nullability, primary keys, thirteen named unique indexes and ordinary query indexes. MySQL string/JSON/date columns become SQLite TEXT and integer/boolean columns become INTEGER. MySQL KEY declarations become CREATE INDEX statements; engine, charset and SET/PREPARE syntax are removed. The nine frozen embedding job manifest columns are included directly in the initial schema. Glevent menu, tenant, capability, original database and unrelated business migrations are excluded.

The schema also adds a local domain event outbox and persistent vector collection/point tables. `LocalDatabase.Migrate` applies this migration in one transaction and records the version, checksum and UTC application time in `assistant_schema_migration`. The initial release recorded SHA256 `7a539c15b302e6f14dfd829dea61295de38de377bd687ccabd5d0cc8f4eeb148`. The current file adds Chinese documentation comments only; startup accepts that exact initial checksum for already-migrated databases while fresh databases record the current checksum below. Future schema changes must use a new migration.

SHA256: `26f2f121c55fa62deb4ee24225c8f401acc1dcf0fbfdd240142524461c3661e0`

## Verification

A real SQLite proof ran successfully against a disposable local database:

- Migration runs twice with one journal record.
- Twenty business tables, all thirteen source unique indexes, and all 368 mapped ORM columns exist, including document/version/chunk and embedding job manifests.
- Multiple absent creation idempotency keys coexist; duplicate present keys fail.
- Active and tombstoned Q&A records coexist; duplicate active records fail. Source composite uniqueness still applies to tombstones.
- Forced transaction failure rolls back both the queued aggregate command and its domain outbox event. Successful commit persists each once; a subsequent empty commit adds nothing.
- Vectors survive index instance changes and rank by actual cosine similarity. Allowed chunk IDs filter retrieval. Readback verifies stored vectors and dimension changes fail closed.

## Boundaries

`LocalDbContext` implements the transaction queue used by the extracted repositories. Runtime methods that originally performed immediate ORM state updates retain that behavior; this extraction does not broaden their transaction boundaries. The outbox is a durable local audit/event record, without a distributed message delivery worker.

The local vector implementation reads the selected collection and calculates cosine similarity in the application process. It uses vectors from the configured embedding provider and does not invent embeddings. This is intended for a single local installation with a modest corpus; configure the existing Qdrant adapter for a larger corpus. Source Q&A `(knowledge_base_id, normalized_question, is_deleted)` uniqueness is preserved, including its restriction on repeated tombstone keys.
