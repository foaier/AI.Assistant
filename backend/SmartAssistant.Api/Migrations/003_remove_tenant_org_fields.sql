/*
 * 移除独立应用不使用的租户与组织字段。
 * 直接删除列以保留记录、写入顺序及现有外键，不重建父子表。
 * 唯一约束改为按业务标识生效，若历史数据冲突则整次迁移回滚。
 */

/*
 * 先移除引用待删除列的索引，避免 SQLite 拒绝修改表结构。
 */

DROP INDEX ix_assistant_conversation_scope;

DROP INDEX ix_assistant_draft_scope;

DROP INDEX ix_assistant_knowledge_base_scope;

DROP INDEX ix_assistant_knowledge_chunk_scope;

DROP INDEX ix_assistant_knowledge_chunking_policy_scope;

DROP INDEX ix_assistant_knowledge_document_scope;

DROP INDEX ix_assistant_knowledge_document_version_scope;

DROP INDEX ix_assistant_knowledge_embedding_policy_scope;

DROP INDEX ix_assistant_knowledge_index_job_generation;

DROP INDEX ix_assistant_knowledge_index_job_scope;

DROP INDEX ix_assistant_knowledge_qna_scope;

DROP INDEX ix_assistant_policy_revision_scope;

DROP INDEX ix_assistant_policy_scope;

DROP INDEX ix_assistant_preview_scope;

DROP INDEX ix_assistant_published_version_public;

DROP INDEX ix_assistant_run_scope;

DROP INDEX ix_assistant_session_scope;

DROP INDEX ix_assistant_turn_scope;

DROP INDEX uq_assistant_draft_scope_idempotency;

DROP INDEX uq_assistant_knowledge_chunking_policy_scope;

DROP INDEX uq_assistant_knowledge_document_asset;

DROP INDEX uq_assistant_knowledge_embedding_policy_scope;

DROP INDEX uq_assistant_publication_pointer_scope;

DROP INDEX uq_assistant_published_version_scope_no;

/*
 * assistant_anonymous_session 仅保留业务数据及业务关联。
 */

ALTER TABLE assistant_anonymous_session DROP COLUMN tenant_id;

ALTER TABLE assistant_anonymous_session DROP COLUMN org_id;

/*
 * assistant_conversation 仅保留业务数据及业务关联。
 */

ALTER TABLE assistant_conversation DROP COLUMN tenant_id;

ALTER TABLE assistant_conversation DROP COLUMN org_id;

/*
 * assistant_draft 仅保留业务数据及业务关联。
 */

ALTER TABLE assistant_draft DROP COLUMN tenant_id;

ALTER TABLE assistant_draft DROP COLUMN org_id;

/*
 * assistant_knowledge_base 仅保留业务数据及业务关联。
 */

ALTER TABLE assistant_knowledge_base DROP COLUMN tenant_id;

ALTER TABLE assistant_knowledge_base DROP COLUMN org_id;

/*
 * assistant_knowledge_chunk 仅保留业务数据及业务关联。
 */

ALTER TABLE assistant_knowledge_chunk DROP COLUMN tenant_id;

ALTER TABLE assistant_knowledge_chunk DROP COLUMN org_id;

/*
 * assistant_knowledge_chunking_policy 仅保留业务数据及业务关联。
 */

ALTER TABLE assistant_knowledge_chunking_policy DROP COLUMN tenant_id;

ALTER TABLE assistant_knowledge_chunking_policy DROP COLUMN org_id;

/*
 * assistant_knowledge_document 仅保留业务数据及业务关联。
 */

ALTER TABLE assistant_knowledge_document DROP COLUMN tenant_id;

ALTER TABLE assistant_knowledge_document DROP COLUMN org_id;

/*
 * assistant_knowledge_document_version 仅保留业务数据及业务关联。
 */

ALTER TABLE assistant_knowledge_document_version DROP COLUMN tenant_id;

ALTER TABLE assistant_knowledge_document_version DROP COLUMN org_id;

/*
 * assistant_knowledge_embedding_policy 仅保留业务数据及业务关联。
 */

ALTER TABLE assistant_knowledge_embedding_policy DROP COLUMN tenant_id;

ALTER TABLE assistant_knowledge_embedding_policy DROP COLUMN org_id;

/*
 * assistant_knowledge_index_job 仅保留业务数据及业务关联。
 */

ALTER TABLE assistant_knowledge_index_job DROP COLUMN tenant_id;

ALTER TABLE assistant_knowledge_index_job DROP COLUMN org_id;

/*
 * assistant_knowledge_qna 仅保留业务数据及业务关联。
 */

ALTER TABLE assistant_knowledge_qna DROP COLUMN tenant_id;

ALTER TABLE assistant_knowledge_qna DROP COLUMN org_id;

/*
 * assistant_policy_record 仅保留业务数据及业务关联。
 */

ALTER TABLE assistant_policy_record DROP COLUMN tenant_id;

ALTER TABLE assistant_policy_record DROP COLUMN org_id;

/*
 * assistant_policy_revision 仅保留业务数据及业务关联。
 */

ALTER TABLE assistant_policy_revision DROP COLUMN tenant_id;

ALTER TABLE assistant_policy_revision DROP COLUMN org_id;

/*
 * assistant_preview_snapshot 仅保留业务数据及业务关联。
 */

ALTER TABLE assistant_preview_snapshot DROP COLUMN tenant_id;

ALTER TABLE assistant_preview_snapshot DROP COLUMN org_id;

/*
 * assistant_publication_pointer 仅保留业务数据及业务关联。
 */

ALTER TABLE assistant_publication_pointer DROP COLUMN tenant_id;

ALTER TABLE assistant_publication_pointer DROP COLUMN org_id;

/*
 * assistant_published_version 仅保留业务数据及业务关联。
 */

ALTER TABLE assistant_published_version DROP COLUMN tenant_id;

ALTER TABLE assistant_published_version DROP COLUMN org_id;

/*
 * assistant_run 仅保留业务数据及业务关联。
 */

ALTER TABLE assistant_run DROP COLUMN tenant_id;

ALTER TABLE assistant_run DROP COLUMN org_id;

/*
 * assistant_turn 仅保留业务数据及业务关联。
 */

ALTER TABLE assistant_turn DROP COLUMN tenant_id;

ALTER TABLE assistant_turn DROP COLUMN org_id;

/*
 * 按实际业务字段恢复 assistant_conversation 的查询索引。
 */

CREATE INDEX ix_assistant_conversation_scope ON assistant_conversation (
    assistant_id,
    published_version_id,
    execution_mode
);

/*
 * 按实际业务字段恢复 assistant_knowledge_chunk 的查询索引。
 */

CREATE INDEX ix_assistant_knowledge_chunk_scope ON assistant_knowledge_chunk (
    document_version_id,
    chunk_index
);

/*
 * 按实际业务字段恢复 assistant_knowledge_chunking_policy 的查询索引。
 */

CREATE INDEX ix_assistant_knowledge_chunking_policy_scope ON assistant_knowledge_chunking_policy (
    knowledge_base_id,
    revision
);

/*
 * 按实际业务字段恢复 assistant_knowledge_document 的查询索引。
 */

CREATE INDEX ix_assistant_knowledge_document_scope ON assistant_knowledge_document (
    knowledge_base_id
);

/*
 * 按实际业务字段恢复 assistant_knowledge_document_version 的查询索引。
 */

CREATE INDEX ix_assistant_knowledge_document_version_scope ON assistant_knowledge_document_version (
    document_id,
    version_no
);

/*
 * 按实际业务字段恢复 assistant_knowledge_embedding_policy 的查询索引。
 */

CREATE INDEX ix_assistant_knowledge_embedding_policy_scope ON assistant_knowledge_embedding_policy (
    knowledge_base_id,
    revision
);

/*
 * 按实际业务字段恢复 assistant_knowledge_index_job 的查询索引。
 */

CREATE INDEX ix_assistant_knowledge_index_job_generation ON assistant_knowledge_index_job (
    knowledge_base_id,
    generation,
    status
);

/*
 * 按实际业务字段恢复 assistant_knowledge_index_job 的查询索引。
 */

CREATE INDEX ix_assistant_knowledge_index_job_scope ON assistant_knowledge_index_job (
    knowledge_base_id,
    status
);

/*
 * 按实际业务字段恢复 assistant_knowledge_qna 的查询索引。
 */

CREATE INDEX ix_assistant_knowledge_qna_scope ON assistant_knowledge_qna (
    knowledge_base_id,
    enabled,
    sort_order
);

/*
 * 按实际业务字段恢复 assistant_policy_revision 的查询索引。
 */

CREATE INDEX ix_assistant_policy_revision_scope ON assistant_policy_revision (
    kind
);

/*
 * 按实际业务字段恢复 assistant_policy_record 的查询索引。
 */

CREATE INDEX ix_assistant_policy_scope ON assistant_policy_record (
    kind
);

/*
 * 按实际业务字段恢复 assistant_preview_snapshot 的查询索引。
 */

CREATE INDEX ix_assistant_preview_scope ON assistant_preview_snapshot (
    assistant_id,
    actor_id
);

/*
 * 按实际业务字段恢复 assistant_published_version 的查询索引。
 */

CREATE INDEX ix_assistant_published_version_public ON assistant_published_version (
    assistant_key,
    is_revoked
);

/*
 * 按实际业务字段恢复 assistant_run 的查询索引。
 */

CREATE INDEX ix_assistant_run_scope ON assistant_run (
    assistant_id,
    published_version_id,
    execution_mode
);

/*
 * 按实际业务字段恢复 assistant_anonymous_session 的查询索引。
 */

CREATE INDEX ix_assistant_session_scope ON assistant_anonymous_session (
    assistant_id,
    published_version_id,
    execution_mode
);

/*
 * 按实际业务字段恢复 assistant_turn 的查询索引。
 */

CREATE INDEX ix_assistant_turn_scope ON assistant_turn (
    assistant_id,
    published_version_id,
    execution_mode
);

/*
 * 按实际业务字段恢复 assistant_draft 的唯一约束。
 */

CREATE UNIQUE INDEX uq_assistant_draft_scope_idempotency ON assistant_draft (
    creation_idempotency_key
);

/*
 * 按实际业务字段恢复 assistant_knowledge_chunking_policy 的唯一约束。
 */

CREATE UNIQUE INDEX uq_assistant_knowledge_chunking_policy_scope ON assistant_knowledge_chunking_policy (
    knowledge_base_id
);

/*
 * 按实际业务字段恢复 assistant_knowledge_document 的唯一约束。
 */

CREATE UNIQUE INDEX uq_assistant_knowledge_document_asset ON assistant_knowledge_document (
    asset_id
);

/*
 * 按实际业务字段恢复 assistant_knowledge_embedding_policy 的唯一约束。
 */

CREATE UNIQUE INDEX uq_assistant_knowledge_embedding_policy_scope ON assistant_knowledge_embedding_policy (
    knowledge_base_id
);

/*
 * 按实际业务字段恢复 assistant_publication_pointer 的唯一约束。
 */

CREATE UNIQUE INDEX uq_assistant_publication_pointer_scope ON assistant_publication_pointer (
    assistant_id
);

/*
 * 按实际业务字段恢复 assistant_published_version 的唯一约束。
 */

CREATE UNIQUE INDEX uq_assistant_published_version_scope_no ON assistant_published_version (
    assistant_id,
    version_no
);

/*
 * 旧集合名含两个各十二位的范围摘要，新集合名仅使用知识库、配置与代次。
 * 先建立新集合，再转移向量，最后删除旧集合，始终保持外键有效。
 * 目标集合重名时由唯一约束中止迁移，避免覆盖已有索引。
 */
CREATE TEMP TABLE assistant_collection_rename (
    old_name TEXT NOT NULL PRIMARY KEY,
    new_name TEXT NOT NULL UNIQUE
);

INSERT INTO assistant_collection_rename (old_name, new_name)
SELECT name, 'assistant_kb_' || substr(name, 40)
FROM assistant_vector_collection
WHERE name GLOB 'assistant_kb_[0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f]_[0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f]_[0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f]_[0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f]_g[0-9]*'
    AND substr(name, 67) NOT GLOB '*[^0-9]*';

INSERT INTO assistant_vector_collection (name, dimension)
SELECT mapping.new_name, collection.dimension
FROM assistant_vector_collection AS collection
JOIN assistant_collection_rename AS mapping ON mapping.old_name = collection.name;

UPDATE assistant_vector_point
SET collection_name = (
    SELECT new_name
    FROM assistant_collection_rename
    WHERE old_name = assistant_vector_point.collection_name
)
WHERE collection_name IN (SELECT old_name FROM assistant_collection_rename);

DELETE FROM assistant_vector_collection
WHERE name IN (SELECT old_name FROM assistant_collection_rename);

DROP TABLE assistant_collection_rename;
