/*
 * 删除七个通用审计字段，并将关联数据的删除改为外键级联。
 * 已有软删除记录会在移除删除标记前清理，防止历史记录重新出现。
 * 此迁移由执行器在单个事务中应用，初始化迁移保持冻结。
 */

DELETE FROM assistant_draft WHERE is_deleted <> 0;

DELETE FROM assistant_policy_record WHERE is_deleted <> 0;

DELETE FROM assistant_policy_revision WHERE is_deleted <> 0;

DELETE FROM assistant_published_version WHERE is_deleted <> 0;

DELETE FROM assistant_publication_pointer WHERE is_deleted <> 0;

DELETE FROM assistant_anonymous_session WHERE is_deleted <> 0;

DELETE FROM assistant_conversation WHERE is_deleted <> 0;

DELETE FROM assistant_turn WHERE is_deleted <> 0;

DELETE FROM assistant_run WHERE is_deleted <> 0;

DELETE FROM assistant_message WHERE is_deleted <> 0;

DELETE FROM assistant_tool_call WHERE is_deleted <> 0;

DELETE FROM assistant_knowledge_base WHERE is_deleted <> 0;

DELETE FROM assistant_knowledge_qna WHERE is_deleted <> 0;

DELETE FROM assistant_knowledge_document WHERE is_deleted <> 0;

DELETE FROM assistant_knowledge_document_version WHERE is_deleted <> 0;

DELETE FROM assistant_knowledge_chunk WHERE is_deleted <> 0;

DELETE FROM assistant_knowledge_chunking_policy WHERE is_deleted <> 0;

DELETE FROM assistant_preview_snapshot WHERE is_deleted <> 0;

DELETE FROM assistant_knowledge_embedding_policy WHERE is_deleted <> 0;

/*
 * 草稿已删除时清理其会话，再按父子层次清理历史关联记录。
 */
DELETE FROM assistant_anonymous_session WHERE assistant_id NOT IN (SELECT id FROM assistant_draft);

DELETE FROM assistant_policy_revision
WHERE record_id IS NOT NULL
    AND NOT EXISTS (
        SELECT 1 FROM assistant_policy_record
        WHERE assistant_policy_record.id = assistant_policy_revision.record_id
    );

DELETE FROM assistant_conversation
WHERE session_id IS NOT NULL
    AND NOT EXISTS (
        SELECT 1 FROM assistant_anonymous_session
        WHERE assistant_anonymous_session.id = assistant_conversation.session_id
    );

DELETE FROM assistant_turn
WHERE conversation_id IS NOT NULL
    AND NOT EXISTS (
        SELECT 1 FROM assistant_conversation
        WHERE assistant_conversation.id = assistant_turn.conversation_id
    );

DELETE FROM assistant_run
WHERE turn_id IS NOT NULL
    AND NOT EXISTS (
        SELECT 1 FROM assistant_turn
        WHERE assistant_turn.id = assistant_run.turn_id
    );

DELETE FROM assistant_message
WHERE conversation_id IS NOT NULL
    AND NOT EXISTS (
        SELECT 1 FROM assistant_conversation
        WHERE assistant_conversation.id = assistant_message.conversation_id
    );

DELETE FROM assistant_message
WHERE turn_id IS NOT NULL
    AND NOT EXISTS (
        SELECT 1 FROM assistant_turn
        WHERE assistant_turn.id = assistant_message.turn_id
    );

DELETE FROM assistant_message
WHERE run_id IS NOT NULL
    AND NOT EXISTS (
        SELECT 1 FROM assistant_run
        WHERE assistant_run.id = assistant_message.run_id
    );

DELETE FROM assistant_tool_call
WHERE run_id IS NOT NULL
    AND NOT EXISTS (
        SELECT 1 FROM assistant_run
        WHERE assistant_run.id = assistant_tool_call.run_id
    );

DELETE FROM assistant_knowledge_qna
WHERE knowledge_base_id IS NOT NULL
    AND NOT EXISTS (
        SELECT 1 FROM assistant_knowledge_base
        WHERE assistant_knowledge_base.id = assistant_knowledge_qna.knowledge_base_id
    );

DELETE FROM assistant_knowledge_document
WHERE knowledge_base_id IS NOT NULL
    AND NOT EXISTS (
        SELECT 1 FROM assistant_knowledge_base
        WHERE assistant_knowledge_base.id = assistant_knowledge_document.knowledge_base_id
    );

DELETE FROM assistant_knowledge_document_version
WHERE document_id IS NOT NULL
    AND NOT EXISTS (
        SELECT 1 FROM assistant_knowledge_document
        WHERE assistant_knowledge_document.id = assistant_knowledge_document_version.document_id
    );

DELETE FROM assistant_knowledge_chunk
WHERE document_version_id IS NOT NULL
    AND NOT EXISTS (
        SELECT 1 FROM assistant_knowledge_document_version
        WHERE assistant_knowledge_document_version.id = assistant_knowledge_chunk.document_version_id
    );

DELETE FROM assistant_knowledge_chunking_policy
WHERE knowledge_base_id IS NOT NULL
    AND NOT EXISTS (
        SELECT 1 FROM assistant_knowledge_base
        WHERE assistant_knowledge_base.id = assistant_knowledge_chunking_policy.knowledge_base_id
    );

DELETE FROM assistant_knowledge_index_job
WHERE document_version_id IS NOT NULL
    AND NOT EXISTS (
        SELECT 1 FROM assistant_knowledge_document_version
        WHERE assistant_knowledge_document_version.id = assistant_knowledge_index_job.document_version_id
    );

DELETE FROM assistant_knowledge_embedding_policy
WHERE knowledge_base_id IS NOT NULL
    AND NOT EXISTS (
        SELECT 1 FROM assistant_knowledge_base
        WHERE assistant_knowledge_base.id = assistant_knowledge_embedding_policy.knowledge_base_id
    );

DELETE FROM assistant_publication_pointer WHERE assistant_id NOT IN (SELECT id FROM assistant_draft);

DELETE FROM assistant_published_version WHERE assistant_id NOT IN (SELECT id FROM assistant_draft);

DELETE FROM assistant_publication_pointer
WHERE current_published_version_id IS NOT NULL
    AND current_published_version_id NOT IN (
        SELECT id FROM assistant_published_version
    );

DELETE FROM assistant_preview_snapshot WHERE assistant_id NOT IN (SELECT id FROM assistant_draft);

/*
 * 重建 assistant_draft，保留业务字段及原有写入顺序。
 */

CREATE TABLE assistant_draft__new (
    id TEXT NOT NULL PRIMARY KEY,
    tenant_id TEXT NOT NULL,
    org_id TEXT NOT NULL,
    name TEXT NOT NULL,
    description TEXT NULL,
    draft_json TEXT NOT NULL,
    draft_revision INTEGER NOT NULL,
    creation_idempotency_key TEXT NULL
);

INSERT INTO assistant_draft__new (
    rowid,
    id,
    tenant_id,
    org_id,
    name,
    description,
    draft_json,
    draft_revision,
    creation_idempotency_key
)
SELECT
    rowid,
    id,
    tenant_id,
    org_id,
    name,
    description,
    draft_json,
    draft_revision,
    creation_idempotency_key
FROM assistant_draft;

DROP TABLE assistant_draft;

ALTER TABLE assistant_draft__new RENAME TO assistant_draft;

/*
 * 重建 assistant_policy_record，保留业务字段及原有写入顺序。
 */

CREATE TABLE assistant_policy_record__new (
    id TEXT NOT NULL PRIMARY KEY,
    tenant_id TEXT NOT NULL,
    org_id TEXT NOT NULL,
    kind TEXT NOT NULL,
    name TEXT NOT NULL,
    business_code TEXT NULL,
    revision INTEGER NOT NULL,
    enabled INTEGER NOT NULL DEFAULT 1,
    settings_json TEXT NOT NULL
);

INSERT INTO assistant_policy_record__new (
    rowid,
    id,
    tenant_id,
    org_id,
    kind,
    name,
    business_code,
    revision,
    enabled,
    settings_json
)
SELECT
    rowid,
    id,
    tenant_id,
    org_id,
    kind,
    name,
    business_code,
    revision,
    enabled,
    settings_json
FROM assistant_policy_record;

DROP TABLE assistant_policy_record;

ALTER TABLE assistant_policy_record__new RENAME TO assistant_policy_record;

/*
 * 重建 assistant_policy_revision，保留业务字段及原有写入顺序。
 */

CREATE TABLE assistant_policy_revision__new (
    id TEXT NOT NULL PRIMARY KEY,
    record_id TEXT NOT NULL,
    tenant_id TEXT NOT NULL,
    org_id TEXT NOT NULL,
    kind TEXT NOT NULL,
    revision INTEGER NOT NULL,
    enabled INTEGER NOT NULL,
    settings_json TEXT NOT NULL,
    FOREIGN KEY (record_id) REFERENCES assistant_policy_record(id) ON DELETE CASCADE
);

INSERT INTO assistant_policy_revision__new (
    rowid,
    id,
    record_id,
    tenant_id,
    org_id,
    kind,
    revision,
    enabled,
    settings_json
)
SELECT
    rowid,
    id,
    record_id,
    tenant_id,
    org_id,
    kind,
    revision,
    enabled,
    settings_json
FROM assistant_policy_revision;

DROP TABLE assistant_policy_revision;

ALTER TABLE assistant_policy_revision__new RENAME TO assistant_policy_revision;

/*
 * 重建 assistant_published_version，保留业务字段及原有写入顺序。
 */

CREATE TABLE assistant_published_version__new (
    id TEXT NOT NULL PRIMARY KEY,
    assistant_key TEXT NOT NULL,
    assistant_id TEXT NOT NULL,
    tenant_id TEXT NOT NULL,
    org_id TEXT NOT NULL,
    version_no INTEGER NOT NULL,
    name TEXT NOT NULL,
    description TEXT NULL,
    snapshot_json TEXT NOT NULL,
    snapshot_hash TEXT NOT NULL,
    is_revoked INTEGER NOT NULL DEFAULT 0,
    revoked_at TEXT NULL,
    revoked_by TEXT NULL,
    published_at TEXT NOT NULL,
    published_by TEXT NOT NULL
);

INSERT INTO assistant_published_version__new (
    rowid,
    id,
    assistant_key,
    assistant_id,
    tenant_id,
    org_id,
    version_no,
    name,
    description,
    snapshot_json,
    snapshot_hash,
    is_revoked,
    revoked_at,
    revoked_by,
    published_at,
    published_by
)
SELECT
    rowid,
    id,
    assistant_key,
    assistant_id,
    tenant_id,
    org_id,
    version_no,
    name,
    description,
    snapshot_json,
    snapshot_hash,
    is_revoked,
    revoked_at,
    revoked_by,
    published_at,
    published_by
FROM assistant_published_version;

DROP TABLE assistant_published_version;

ALTER TABLE assistant_published_version__new RENAME TO assistant_published_version;

/*
 * 重建 assistant_publication_pointer，保留业务字段及原有写入顺序。
 */

CREATE TABLE assistant_publication_pointer__new (
    id TEXT NOT NULL PRIMARY KEY,
    assistant_key TEXT NOT NULL,
    assistant_id TEXT NOT NULL,
    tenant_id TEXT NOT NULL,
    org_id TEXT NOT NULL,
    current_published_version_id TEXT NULL,
    current_version_no INTEGER NOT NULL DEFAULT 0,
    pointer_revision INTEGER NOT NULL DEFAULT 0,
    status INTEGER NOT NULL DEFAULT 0
);

INSERT INTO assistant_publication_pointer__new (
    rowid,
    id,
    assistant_key,
    assistant_id,
    tenant_id,
    org_id,
    current_published_version_id,
    current_version_no,
    pointer_revision,
    status
)
SELECT
    rowid,
    id,
    assistant_key,
    assistant_id,
    tenant_id,
    org_id,
    current_published_version_id,
    current_version_no,
    pointer_revision,
    status
FROM assistant_publication_pointer;

DROP TABLE assistant_publication_pointer;

ALTER TABLE assistant_publication_pointer__new RENAME TO assistant_publication_pointer;

/*
 * 重建 assistant_anonymous_session，保留业务字段及原有写入顺序。
 */

CREATE TABLE assistant_anonymous_session__new (
    id TEXT NOT NULL PRIMARY KEY,
    assistant_id TEXT NOT NULL,
    tenant_id TEXT NOT NULL,
    org_id TEXT NOT NULL,
    published_version_id TEXT NOT NULL,
    subject_hash TEXT NOT NULL,
    locale TEXT NOT NULL,
    execution_mode INTEGER NOT NULL,
    status INTEGER NOT NULL,
    last_seen_at TEXT NOT NULL,
    expires_at TEXT NOT NULL,
    revoked_at TEXT NULL,
    revoked_by TEXT NULL
);

INSERT INTO assistant_anonymous_session__new (
    rowid,
    id,
    assistant_id,
    tenant_id,
    org_id,
    published_version_id,
    subject_hash,
    locale,
    execution_mode,
    status,
    last_seen_at,
    expires_at,
    revoked_at,
    revoked_by
)
SELECT
    rowid,
    id,
    assistant_id,
    tenant_id,
    org_id,
    published_version_id,
    subject_hash,
    locale,
    execution_mode,
    status,
    last_seen_at,
    expires_at,
    revoked_at,
    revoked_by
FROM assistant_anonymous_session;

DROP TABLE assistant_anonymous_session;

ALTER TABLE assistant_anonymous_session__new RENAME TO assistant_anonymous_session;

/*
 * 重建 assistant_conversation，保留业务字段及原有写入顺序。
 */

CREATE TABLE assistant_conversation__new (
    id TEXT NOT NULL PRIMARY KEY,
    session_id TEXT NOT NULL,
    assistant_id TEXT NOT NULL,
    tenant_id TEXT NOT NULL,
    org_id TEXT NOT NULL,
    published_version_id TEXT NOT NULL,
    locale TEXT NOT NULL,
    execution_mode INTEGER NOT NULL,
    status INTEGER NOT NULL,
    last_turn_no INTEGER NOT NULL,
    last_activity_at TEXT NOT NULL,
    FOREIGN KEY (session_id) REFERENCES assistant_anonymous_session(id) ON DELETE CASCADE
);

INSERT INTO assistant_conversation__new (
    rowid,
    id,
    session_id,
    assistant_id,
    tenant_id,
    org_id,
    published_version_id,
    locale,
    execution_mode,
    status,
    last_turn_no,
    last_activity_at
)
SELECT
    rowid,
    id,
    session_id,
    assistant_id,
    tenant_id,
    org_id,
    published_version_id,
    locale,
    execution_mode,
    status,
    last_turn_no,
    last_activity_at
FROM assistant_conversation;

DROP TABLE assistant_conversation;

ALTER TABLE assistant_conversation__new RENAME TO assistant_conversation;

/*
 * 重建 assistant_turn，保留业务字段及原有写入顺序。
 */

CREATE TABLE assistant_turn__new (
    id TEXT NOT NULL PRIMARY KEY,
    conversation_id TEXT NOT NULL,
    session_id TEXT NOT NULL,
    assistant_id TEXT NOT NULL,
    tenant_id TEXT NOT NULL,
    org_id TEXT NOT NULL,
    published_version_id TEXT NOT NULL,
    client_message_id TEXT NOT NULL,
    request_hash TEXT NOT NULL,
    turn_no INTEGER NOT NULL,
    execution_mode INTEGER NOT NULL,
    status INTEGER NOT NULL,
    completed_at TEXT NULL,
    failure_code TEXT NULL,
    FOREIGN KEY (conversation_id) REFERENCES assistant_conversation(id) ON DELETE CASCADE
);

INSERT INTO assistant_turn__new (
    rowid,
    id,
    conversation_id,
    session_id,
    assistant_id,
    tenant_id,
    org_id,
    published_version_id,
    client_message_id,
    request_hash,
    turn_no,
    execution_mode,
    status,
    completed_at,
    failure_code
)
SELECT
    rowid,
    id,
    conversation_id,
    session_id,
    assistant_id,
    tenant_id,
    org_id,
    published_version_id,
    client_message_id,
    request_hash,
    turn_no,
    execution_mode,
    status,
    completed_at,
    failure_code
FROM assistant_turn;

DROP TABLE assistant_turn;

ALTER TABLE assistant_turn__new RENAME TO assistant_turn;

/*
 * 重建 assistant_run，保留业务字段及原有写入顺序。
 */

CREATE TABLE assistant_run__new (
    id TEXT NOT NULL PRIMARY KEY,
    turn_id TEXT NOT NULL,
    conversation_id TEXT NOT NULL,
    session_id TEXT NOT NULL,
    assistant_id TEXT NOT NULL,
    tenant_id TEXT NOT NULL,
    org_id TEXT NOT NULL,
    published_version_id TEXT NOT NULL,
    execution_mode INTEGER NOT NULL,
    provider_profile_ref TEXT NOT NULL,
    model TEXT NOT NULL,
    status INTEGER NOT NULL,
    sequence_head INTEGER NOT NULL,
    started_at TEXT NULL,
    completed_at TEXT NULL,
    failure_code TEXT NULL,
    prompt_tokens INTEGER NULL,
    completion_tokens INTEGER NULL,
    total_tokens INTEGER NULL,
    FOREIGN KEY (turn_id) REFERENCES assistant_turn(id) ON DELETE CASCADE
);

INSERT INTO assistant_run__new (
    rowid,
    id,
    turn_id,
    conversation_id,
    session_id,
    assistant_id,
    tenant_id,
    org_id,
    published_version_id,
    execution_mode,
    provider_profile_ref,
    model,
    status,
    sequence_head,
    started_at,
    completed_at,
    failure_code,
    prompt_tokens,
    completion_tokens,
    total_tokens
)
SELECT
    rowid,
    id,
    turn_id,
    conversation_id,
    session_id,
    assistant_id,
    tenant_id,
    org_id,
    published_version_id,
    execution_mode,
    provider_profile_ref,
    model,
    status,
    sequence_head,
    started_at,
    completed_at,
    failure_code,
    prompt_tokens,
    completion_tokens,
    total_tokens
FROM assistant_run;

DROP TABLE assistant_run;

ALTER TABLE assistant_run__new RENAME TO assistant_run;

/*
 * 重建 assistant_message，保留业务字段及原有写入顺序。
 */

CREATE TABLE assistant_message__new (
    id TEXT NOT NULL PRIMARY KEY,
    conversation_id TEXT NOT NULL,
    turn_id TEXT NOT NULL,
    run_id TEXT NULL,
    role INTEGER NOT NULL,
    status INTEGER NOT NULL,
    content_json TEXT NOT NULL,
    completed_at TEXT NULL,
    FOREIGN KEY (conversation_id) REFERENCES assistant_conversation(id) ON DELETE CASCADE,
    FOREIGN KEY (turn_id) REFERENCES assistant_turn(id) ON DELETE CASCADE,
    FOREIGN KEY (run_id) REFERENCES assistant_run(id) ON DELETE CASCADE
);

INSERT INTO assistant_message__new (
    rowid,
    id,
    conversation_id,
    turn_id,
    run_id,
    role,
    status,
    content_json,
    completed_at
)
SELECT
    rowid,
    id,
    conversation_id,
    turn_id,
    run_id,
    role,
    status,
    content_json,
    completed_at
FROM assistant_message;

DROP TABLE assistant_message;

ALTER TABLE assistant_message__new RENAME TO assistant_message;

/*
 * 重建 assistant_tool_call，保留业务字段及原有写入顺序。
 */

CREATE TABLE assistant_tool_call__new (
    id TEXT NOT NULL PRIMARY KEY,
    run_id TEXT NOT NULL,
    tool_id TEXT NOT NULL,
    contract_version TEXT NOT NULL,
    input_hash TEXT NOT NULL,
    status INTEGER NOT NULL,
    output_json TEXT NULL,
    failure_code TEXT NULL,
    completed_at TEXT NULL,
    duration_ms REAL NULL,
    FOREIGN KEY (run_id) REFERENCES assistant_run(id) ON DELETE CASCADE
);

/*
 * 旧工具调用的耗时在删除创建时间前换算为毫秒，保留已有统计结果。
 */
INSERT INTO assistant_tool_call__new (
    rowid,
    id,
    run_id,
    tool_id,
    contract_version,
    input_hash,
    status,
    output_json,
    failure_code,
    completed_at,
    duration_ms
)
SELECT
    rowid,
    id,
    run_id,
    tool_id,
    contract_version,
    input_hash,
    status,
    output_json,
    failure_code,
    completed_at,
    CASE WHEN completed_at IS NOT NULL THEN
        MAX(0.0, (julianday(completed_at) - julianday(created_at)) * 86400000.0)
    ELSE NULL END
FROM assistant_tool_call;

DROP TABLE assistant_tool_call;

ALTER TABLE assistant_tool_call__new RENAME TO assistant_tool_call;

/*
 * 重建 assistant_knowledge_base，保留业务字段及原有写入顺序。
 */

CREATE TABLE assistant_knowledge_base__new (
    id TEXT NOT NULL PRIMARY KEY,
    tenant_id TEXT NOT NULL,
    org_id TEXT NOT NULL,
    name TEXT NOT NULL,
    description TEXT NULL
);

INSERT INTO assistant_knowledge_base__new (
    rowid,
    id,
    tenant_id,
    org_id,
    name,
    description
)
SELECT rowid, id, tenant_id, org_id, name, description FROM assistant_knowledge_base;

DROP TABLE assistant_knowledge_base;

ALTER TABLE assistant_knowledge_base__new RENAME TO assistant_knowledge_base;

/*
 * 重建 assistant_knowledge_qna，保留业务字段及原有写入顺序。
 */

CREATE TABLE assistant_knowledge_qna__new (
    id TEXT NOT NULL PRIMARY KEY,
    knowledge_base_id TEXT NOT NULL,
    tenant_id TEXT NOT NULL,
    org_id TEXT NOT NULL,
    question TEXT NOT NULL,
    normalized_question TEXT NOT NULL,
    answer TEXT NULL,
    enabled INTEGER NOT NULL DEFAULT 1,
    sort_order INTEGER NOT NULL DEFAULT 0,
    show_as_suggestion INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (knowledge_base_id) REFERENCES assistant_knowledge_base(id) ON DELETE CASCADE
);

INSERT INTO assistant_knowledge_qna__new (
    rowid,
    id,
    knowledge_base_id,
    tenant_id,
    org_id,
    question,
    normalized_question,
    answer,
    enabled,
    sort_order,
    show_as_suggestion
)
SELECT
    rowid,
    id,
    knowledge_base_id,
    tenant_id,
    org_id,
    question,
    normalized_question,
    answer,
    enabled,
    sort_order,
    show_as_suggestion
FROM assistant_knowledge_qna;

DROP TABLE assistant_knowledge_qna;

ALTER TABLE assistant_knowledge_qna__new RENAME TO assistant_knowledge_qna;

/*
 * 重建 assistant_knowledge_document，保留业务字段及原有写入顺序。
 */

CREATE TABLE assistant_knowledge_document__new (
    id TEXT NOT NULL PRIMARY KEY,
    tenant_id TEXT NOT NULL,
    org_id TEXT NOT NULL,
    knowledge_base_id TEXT NOT NULL,
    asset_id TEXT NOT NULL,
    file_name TEXT NOT NULL,
    content_type TEXT NOT NULL,
    size_bytes INTEGER NOT NULL,
    current_version_no INTEGER NOT NULL DEFAULT 0,
    status TEXT NOT NULL DEFAULT 'uploaded',
    last_error_code TEXT NULL,
    FOREIGN KEY (knowledge_base_id) REFERENCES assistant_knowledge_base(id) ON DELETE CASCADE
);

INSERT INTO assistant_knowledge_document__new (
    rowid,
    id,
    tenant_id,
    org_id,
    knowledge_base_id,
    asset_id,
    file_name,
    content_type,
    size_bytes,
    current_version_no,
    status,
    last_error_code
)
SELECT
    rowid,
    id,
    tenant_id,
    org_id,
    knowledge_base_id,
    asset_id,
    file_name,
    content_type,
    size_bytes,
    current_version_no,
    status,
    last_error_code
FROM assistant_knowledge_document;

DROP TABLE assistant_knowledge_document;

ALTER TABLE assistant_knowledge_document__new RENAME TO assistant_knowledge_document;

/*
 * 重建 assistant_knowledge_document_version，保留业务字段及原有写入顺序。
 */

CREATE TABLE assistant_knowledge_document_version__new (
    id TEXT NOT NULL PRIMARY KEY,
    tenant_id TEXT NOT NULL,
    org_id TEXT NOT NULL,
    document_id TEXT NOT NULL,
    version_no INTEGER NOT NULL,
    content_hash TEXT NOT NULL,
    parser_version TEXT NOT NULL,
    chunk_strategy TEXT NOT NULL,
    chunk_policy_json TEXT NOT NULL,
    status TEXT NOT NULL DEFAULT 'uploaded',
    chunk_count INTEGER NOT NULL DEFAULT 0,
    index_status TEXT NOT NULL DEFAULT 'queued',
    indexed_at TEXT NULL,
    error_code TEXT NULL,
    FOREIGN KEY (document_id) REFERENCES assistant_knowledge_document(id) ON DELETE CASCADE
);

INSERT INTO assistant_knowledge_document_version__new (
    rowid,
    id,
    tenant_id,
    org_id,
    document_id,
    version_no,
    content_hash,
    parser_version,
    chunk_strategy,
    chunk_policy_json,
    status,
    chunk_count,
    index_status,
    indexed_at,
    error_code
)
SELECT
    rowid,
    id,
    tenant_id,
    org_id,
    document_id,
    version_no,
    content_hash,
    parser_version,
    chunk_strategy,
    chunk_policy_json,
    status,
    chunk_count,
    index_status,
    indexed_at,
    error_code
FROM assistant_knowledge_document_version;

DROP TABLE assistant_knowledge_document_version;

ALTER TABLE assistant_knowledge_document_version__new RENAME TO assistant_knowledge_document_version;

/*
 * 重建 assistant_knowledge_chunk，保留业务字段及原有写入顺序。
 */

CREATE TABLE assistant_knowledge_chunk__new (
    id TEXT NOT NULL PRIMARY KEY,
    tenant_id TEXT NOT NULL,
    org_id TEXT NOT NULL,
    document_version_id TEXT NOT NULL,
    chunk_index INTEGER NOT NULL,
    content_hash TEXT NOT NULL,
    content TEXT NOT NULL,
    token_count INTEGER NOT NULL DEFAULT 0,
    point_id TEXT NULL,
    FOREIGN KEY (document_version_id) REFERENCES assistant_knowledge_document_version(id) ON DELETE CASCADE
);

INSERT INTO assistant_knowledge_chunk__new (
    rowid,
    id,
    tenant_id,
    org_id,
    document_version_id,
    chunk_index,
    content_hash,
    content,
    token_count,
    point_id
)
SELECT
    rowid,
    id,
    tenant_id,
    org_id,
    document_version_id,
    chunk_index,
    content_hash,
    content,
    token_count,
    point_id
FROM assistant_knowledge_chunk;

DROP TABLE assistant_knowledge_chunk;

ALTER TABLE assistant_knowledge_chunk__new RENAME TO assistant_knowledge_chunk;

/*
 * 重建 assistant_knowledge_chunking_policy，保留业务字段及原有写入顺序。
 */

CREATE TABLE assistant_knowledge_chunking_policy__new (
    id TEXT NOT NULL PRIMARY KEY,
    tenant_id TEXT NOT NULL,
    org_id TEXT NOT NULL,
    knowledge_base_id TEXT NOT NULL,
    mode TEXT NOT NULL DEFAULT 'hybrid',
    max_chunk_size INTEGER NOT NULL DEFAULT 1200,
    min_chunk_size INTEGER NOT NULL DEFAULT 200,
    overlap_size INTEGER NOT NULL DEFAULT 160,
    max_heading_level INTEGER NOT NULL DEFAULT 3,
    preserve_heading_path INTEGER NOT NULL DEFAULT 1,
    merge_short_chunks INTEGER NOT NULL DEFAULT 1,
    revision INTEGER NOT NULL DEFAULT 1,
    FOREIGN KEY (knowledge_base_id) REFERENCES assistant_knowledge_base(id) ON DELETE CASCADE
);

INSERT INTO assistant_knowledge_chunking_policy__new (
    rowid,
    id,
    tenant_id,
    org_id,
    knowledge_base_id,
    mode,
    max_chunk_size,
    min_chunk_size,
    overlap_size,
    max_heading_level,
    preserve_heading_path,
    merge_short_chunks,
    revision
)
SELECT
    rowid,
    id,
    tenant_id,
    org_id,
    knowledge_base_id,
    mode,
    max_chunk_size,
    min_chunk_size,
    overlap_size,
    max_heading_level,
    preserve_heading_path,
    merge_short_chunks,
    revision
FROM assistant_knowledge_chunking_policy;

DROP TABLE assistant_knowledge_chunking_policy;

ALTER TABLE assistant_knowledge_chunking_policy__new RENAME TO assistant_knowledge_chunking_policy;

/*
 * 重建 assistant_knowledge_index_job，保留业务字段及原有写入顺序。
 */

CREATE TABLE assistant_knowledge_index_job__new (
    id TEXT NOT NULL PRIMARY KEY,
    tenant_id TEXT NOT NULL,
    org_id TEXT NOT NULL,
    knowledge_base_id TEXT NOT NULL,
    document_id TEXT NOT NULL,
    document_version_id TEXT NOT NULL,
    version_no INTEGER NOT NULL,
    status TEXT NOT NULL DEFAULT 'queued',
    attempt_count INTEGER NOT NULL DEFAULT 0,
    chunk_count INTEGER NOT NULL DEFAULT 0,
    error_code TEXT NULL,
    started_at TEXT NULL,
    completed_at TEXT NULL,
    generation INTEGER NULL,
    embedding_provider_id TEXT NULL,
    embedding_provider_revision INTEGER NULL,
    embedding_model TEXT NULL,
    embedding_dimension INTEGER NULL,
    chunk_policy_revision INTEGER NULL,
    chunk_policy_json TEXT NULL,
    parser_version TEXT NULL,
    content_hash TEXT NULL,
    FOREIGN KEY (document_version_id) REFERENCES assistant_knowledge_document_version(id) ON DELETE CASCADE
);

INSERT INTO assistant_knowledge_index_job__new (
    rowid,
    id,
    tenant_id,
    org_id,
    knowledge_base_id,
    document_id,
    document_version_id,
    version_no,
    status,
    attempt_count,
    chunk_count,
    error_code,
    started_at,
    completed_at,
    generation,
    embedding_provider_id,
    embedding_provider_revision,
    embedding_model,
    embedding_dimension,
    chunk_policy_revision,
    chunk_policy_json,
    parser_version,
    content_hash
)
SELECT
    rowid,
    id,
    tenant_id,
    org_id,
    knowledge_base_id,
    document_id,
    document_version_id,
    version_no,
    status,
    attempt_count,
    chunk_count,
    error_code,
    started_at,
    completed_at,
    generation,
    embedding_provider_id,
    embedding_provider_revision,
    embedding_model,
    embedding_dimension,
    chunk_policy_revision,
    chunk_policy_json,
    parser_version,
    content_hash
FROM assistant_knowledge_index_job;

DROP TABLE assistant_knowledge_index_job;

ALTER TABLE assistant_knowledge_index_job__new RENAME TO assistant_knowledge_index_job;

/*
 * 重建 assistant_preview_snapshot，保留业务字段及原有写入顺序。
 */

CREATE TABLE assistant_preview_snapshot__new (
    id TEXT NOT NULL PRIMARY KEY,
    tenant_id TEXT NOT NULL,
    org_id TEXT NOT NULL,
    assistant_id TEXT NOT NULL,
    actor_id TEXT NOT NULL,
    draft_revision INTEGER NOT NULL,
    configuration_json TEXT NOT NULL,
    session_id TEXT NOT NULL,
    conversation_id TEXT NOT NULL,
    expires_at TEXT NOT NULL
);

INSERT INTO assistant_preview_snapshot__new (
    rowid,
    id,
    tenant_id,
    org_id,
    assistant_id,
    actor_id,
    draft_revision,
    configuration_json,
    session_id,
    conversation_id,
    expires_at
)
SELECT
    rowid,
    id,
    tenant_id,
    org_id,
    assistant_id,
    actor_id,
    draft_revision,
    configuration_json,
    session_id,
    conversation_id,
    expires_at
FROM assistant_preview_snapshot;

DROP TABLE assistant_preview_snapshot;

ALTER TABLE assistant_preview_snapshot__new RENAME TO assistant_preview_snapshot;

/*
 * 重建 assistant_knowledge_embedding_policy，保留业务字段及原有写入顺序。
 */

CREATE TABLE assistant_knowledge_embedding_policy__new (
    id                       TEXT NOT NULL,
    tenant_id                TEXT NOT NULL,
    org_id                   TEXT NOT NULL,
    knowledge_base_id        TEXT NOT NULL,
    provider_id              TEXT NOT NULL,
    provider_revision        INTEGER NOT NULL,
    model                    TEXT NOT NULL,
    dimension                INTEGER NOT NULL,
    target_generation        INTEGER NOT NULL,
    active_provider_id       TEXT NULL,
    active_provider_revision INTEGER NULL,
    active_model             TEXT NULL,
    active_dimension         INTEGER NULL,
    active_generation        INTEGER NOT NULL DEFAULT 0,
    active_profile_revision  INTEGER NULL,
    status                   TEXT NOT NULL DEFAULT 'rebuilding',
    last_error_code          TEXT NULL,
    revision                 INTEGER NOT NULL DEFAULT 1,
    FOREIGN KEY (knowledge_base_id) REFERENCES assistant_knowledge_base(id) ON DELETE CASCADE
);

INSERT INTO assistant_knowledge_embedding_policy__new (
    rowid,
    id,
    tenant_id,
    org_id,
    knowledge_base_id,
    provider_id,
    provider_revision,
    model,
    dimension,
    target_generation,
    active_provider_id,
    active_provider_revision,
    active_model,
    active_dimension,
    active_generation,
    active_profile_revision,
    status,
    last_error_code,
    revision
)
SELECT
    rowid,
    id,
    tenant_id,
    org_id,
    knowledge_base_id,
    provider_id,
    provider_revision,
    model,
    dimension,
    target_generation,
    active_provider_id,
    active_provider_revision,
    active_model,
    active_dimension,
    active_generation,
    active_profile_revision,
    status,
    last_error_code,
    revision
FROM assistant_knowledge_embedding_policy;

DROP TABLE assistant_knowledge_embedding_policy;

ALTER TABLE assistant_knowledge_embedding_policy__new RENAME TO assistant_knowledge_embedding_policy;

/*
 * 重建 assistant_outbox，保留业务字段及原有写入顺序。
 */

CREATE TABLE assistant_outbox__new (
    id TEXT NOT NULL PRIMARY KEY,
    event_type TEXT NOT NULL,
    payload_json TEXT NOT NULL
);

INSERT INTO assistant_outbox__new (
    rowid,
    id,
    event_type,
    payload_json
)
SELECT rowid, id, event_type, payload_json FROM assistant_outbox;

DROP TABLE assistant_outbox;

ALTER TABLE assistant_outbox__new RENAME TO assistant_outbox;

/*
 * 为 assistant_draft 恢复唯一约束。
 */

CREATE UNIQUE INDEX uq_assistant_draft_scope_idempotency ON assistant_draft (
    tenant_id,
    org_id,
    creation_idempotency_key
);

/*
 * 为 assistant_draft 恢复业务查询索引。
 */

CREATE INDEX ix_assistant_draft_scope ON assistant_draft (tenant_id, org_id);

/*
 * 为 assistant_policy_record 恢复业务查询索引。
 */

CREATE INDEX ix_assistant_policy_scope ON assistant_policy_record (tenant_id, org_id, kind);

/*
 * 为 assistant_policy_revision 恢复唯一约束。
 */

CREATE UNIQUE INDEX uq_assistant_policy_revision ON assistant_policy_revision (record_id, revision);

/*
 * 为 assistant_policy_revision 恢复业务查询索引。
 */

CREATE INDEX ix_assistant_policy_revision_scope ON assistant_policy_revision (tenant_id, org_id, kind);

/*
 * 为 assistant_published_version 恢复唯一约束。
 */

CREATE UNIQUE INDEX uq_assistant_published_version_scope_no ON assistant_published_version (
    tenant_id,
    org_id,
    assistant_id,
    version_no
);

/*
 * 为 assistant_published_version 恢复业务查询索引。
 */

CREATE INDEX ix_assistant_published_version_public ON assistant_published_version (
    assistant_key,
    tenant_id,
    org_id,
    is_revoked
);

/*
 * 为 assistant_published_version 恢复业务查询索引。
 */

CREATE INDEX ix_assistant_published_version_hash ON assistant_published_version (assistant_id, snapshot_hash);

/*
 * 为 assistant_publication_pointer 恢复唯一约束。
 */

CREATE UNIQUE INDEX uq_assistant_publication_pointer_scope ON assistant_publication_pointer (
    tenant_id,
    org_id,
    assistant_id
);

/*
 * 为 assistant_publication_pointer 恢复唯一约束。
 */

CREATE UNIQUE INDEX uq_assistant_publication_pointer_key ON assistant_publication_pointer (assistant_key);

/*
 * 为 assistant_publication_pointer 恢复业务查询索引。
 */

CREATE INDEX ix_assistant_publication_pointer_public ON assistant_publication_pointer (assistant_key, status);

/*
 * 为 assistant_publication_pointer 恢复业务查询索引。
 */

CREATE INDEX ix_assistant_publication_pointer_version ON assistant_publication_pointer (
    current_published_version_id
);

/*
 * 为 assistant_anonymous_session 恢复业务查询索引。
 */

CREATE INDEX ix_assistant_session_scope ON assistant_anonymous_session (
    assistant_id,
    tenant_id,
    org_id,
    published_version_id,
    execution_mode
);

/*
 * 为 assistant_anonymous_session 恢复业务查询索引。
 */

CREATE INDEX ix_assistant_session_subject ON assistant_anonymous_session (
    assistant_id,
    subject_hash,
    expires_at
);

/*
 * 为 assistant_conversation 恢复业务查询索引。
 */

CREATE INDEX ix_assistant_conversation_scope ON assistant_conversation (
    assistant_id,
    tenant_id,
    org_id,
    published_version_id,
    execution_mode
);

/*
 * 为 assistant_conversation 恢复业务查询索引。
 */

CREATE INDEX ix_assistant_conversation_session ON assistant_conversation (session_id, last_activity_at);

/*
 * 为 assistant_turn 恢复唯一约束。
 */

CREATE UNIQUE INDEX uq_assistant_turn_conversation_client_message ON assistant_turn (
    conversation_id,
    client_message_id
);

/*
 * 为 assistant_turn 恢复业务查询索引。
 */

CREATE INDEX ix_assistant_turn_scope ON assistant_turn (
    assistant_id,
    tenant_id,
    org_id,
    published_version_id,
    execution_mode
);

/*
 * 为 assistant_turn 恢复业务查询索引。
 */

CREATE INDEX ix_assistant_turn_conversation_order ON assistant_turn (conversation_id, turn_no);

/*
 * 为 assistant_run 恢复唯一约束。
 */

CREATE UNIQUE INDEX uq_assistant_run_turn ON assistant_run (turn_id);

/*
 * 为 assistant_run 恢复业务查询索引。
 */

CREATE INDEX ix_assistant_run_scope ON assistant_run (
    assistant_id,
    tenant_id,
    org_id,
    published_version_id,
    execution_mode
);

/*
 * 为 assistant_run 恢复业务查询索引。
 */

CREATE INDEX ix_assistant_run_conversation ON assistant_run (conversation_id);

/*
 * 为 assistant_message 恢复业务查询索引。
 */

CREATE INDEX ix_assistant_message_conversation ON assistant_message (conversation_id);

/*
 * 为 assistant_message 恢复业务查询索引。
 */

CREATE INDEX ix_assistant_message_turn ON assistant_message (turn_id);

/*
 * 为 assistant_message 恢复业务查询索引。
 */

CREATE INDEX ix_assistant_message_run ON assistant_message (run_id);

/*
 * 为 assistant_tool_call 恢复业务查询索引。
 */

CREATE INDEX ix_assistant_tool_call_run ON assistant_tool_call (run_id);

/*
 * 为 assistant_tool_call 恢复业务查询索引。
 */

CREATE INDEX ix_assistant_tool_call_tool ON assistant_tool_call (tool_id, contract_version);

/*
 * 为 assistant_knowledge_base 恢复业务查询索引。
 */

CREATE INDEX ix_assistant_knowledge_base_scope ON assistant_knowledge_base (tenant_id, org_id);

/*
 * 为 assistant_knowledge_qna 恢复唯一约束。
 */

CREATE UNIQUE INDEX uq_assistant_knowledge_qna_exact ON assistant_knowledge_qna (
    knowledge_base_id,
    normalized_question
);

/*
 * 为 assistant_knowledge_qna 恢复业务查询索引。
 */

CREATE INDEX ix_assistant_knowledge_qna_scope ON assistant_knowledge_qna (
    tenant_id,
    org_id,
    knowledge_base_id,
    enabled,
    sort_order
);

/*
 * 为 assistant_knowledge_document 恢复唯一约束。
 */

CREATE UNIQUE INDEX uq_assistant_knowledge_document_asset ON assistant_knowledge_document (
    tenant_id,
    org_id,
    asset_id
);

/*
 * 为 assistant_knowledge_document 恢复业务查询索引。
 */

CREATE INDEX ix_assistant_knowledge_document_scope ON assistant_knowledge_document (
    tenant_id,
    org_id,
    knowledge_base_id
);

/*
 * 为 assistant_knowledge_document_version 恢复唯一约束。
 */

CREATE UNIQUE INDEX uq_assistant_knowledge_document_version ON assistant_knowledge_document_version (
    document_id,
    version_no
);

/*
 * 为 assistant_knowledge_document_version 恢复业务查询索引。
 */

CREATE INDEX ix_assistant_knowledge_document_version_scope ON assistant_knowledge_document_version (
    tenant_id,
    org_id,
    document_id,
    version_no
);

/*
 * 为 assistant_knowledge_chunk 恢复唯一约束。
 */

CREATE UNIQUE INDEX uq_assistant_knowledge_chunk_index ON assistant_knowledge_chunk (
    document_version_id,
    chunk_index
);

/*
 * 为 assistant_knowledge_chunk 恢复业务查询索引。
 */

CREATE INDEX ix_assistant_knowledge_chunk_scope ON assistant_knowledge_chunk (
    tenant_id,
    org_id,
    document_version_id,
    chunk_index
);

/*
 * 为 assistant_knowledge_chunking_policy 恢复唯一约束。
 */

CREATE UNIQUE INDEX uq_assistant_knowledge_chunking_policy_scope ON assistant_knowledge_chunking_policy (
    tenant_id,
    org_id,
    knowledge_base_id
);

/*
 * 为 assistant_knowledge_chunking_policy 恢复业务查询索引。
 */

CREATE INDEX ix_assistant_knowledge_chunking_policy_scope ON assistant_knowledge_chunking_policy (
    tenant_id,
    org_id,
    knowledge_base_id,
    revision
);

/*
 * 为 assistant_knowledge_index_job 恢复业务查询索引。
 */

CREATE INDEX ix_assistant_knowledge_index_job_scope ON assistant_knowledge_index_job (
    tenant_id,
    org_id,
    knowledge_base_id,
    status
);

/*
 * 为 assistant_knowledge_index_job 恢复业务查询索引。
 */

CREATE INDEX ix_assistant_knowledge_index_job_version ON assistant_knowledge_index_job (document_version_id);

/*
 * 为 assistant_preview_snapshot 恢复业务查询索引。
 */

CREATE INDEX ix_assistant_preview_scope ON assistant_preview_snapshot (
    tenant_id,
    org_id,
    assistant_id,
    actor_id
);

/*
 * 为 assistant_knowledge_embedding_policy 恢复唯一约束。
 */

CREATE UNIQUE INDEX uq_assistant_knowledge_embedding_policy_scope ON assistant_knowledge_embedding_policy (
    tenant_id,
    org_id,
    knowledge_base_id
);

/*
 * 为 assistant_knowledge_embedding_policy 恢复业务查询索引。
 */

CREATE INDEX ix_assistant_knowledge_embedding_policy_scope ON assistant_knowledge_embedding_policy (
    tenant_id,
    org_id,
    knowledge_base_id,
    revision
);

/*
 * 为 assistant_knowledge_index_job 恢复业务查询索引。
 */

CREATE INDEX ix_assistant_knowledge_index_job_generation ON assistant_knowledge_index_job (
    tenant_id,
    org_id,
    knowledge_base_id,
    generation,
    status
);
