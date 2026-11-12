/*
 * 初始 SQLite 数据库架构，迁移版本 1。
 * 数据表根据 Glevent 136a0011、136a0009 和 136a0012 转换；未来结构变更必须新增迁移。
 */

/*
 * 助手可编辑草稿。保存配置、租户与组织范围、草稿修订号、创建幂等键以及修改和删除审计信息。
 */
CREATE TABLE assistant_draft (
    id TEXT NOT NULL PRIMARY KEY,
    tenant_id TEXT NOT NULL,
    org_id TEXT NOT NULL,
    name TEXT NOT NULL,
    description TEXT NULL,
    draft_json TEXT NOT NULL,
    draft_revision INTEGER NOT NULL,
    is_deleted INTEGER NOT NULL DEFAULT 0,
    deleted_at TEXT NULL,
    deleted_by TEXT NULL,
    created_at TEXT NOT NULL,
    created_by TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    updated_by TEXT NOT NULL,
    creation_idempotency_key TEXT NULL
);
/*
 * 可空创建幂等键在租户和组织范围内唯一，防止重复创建；SQLite 允许多行幂等键为空。
 * 适用表：assistant_draft。
 */
CREATE UNIQUE INDEX uq_assistant_draft_scope_idempotency ON assistant_draft (tenant_id, org_id, creation_idempotency_key);
/*
 * 按租户、组织、删除状态和修改时间查询草稿列表。
 * 适用表：assistant_draft。
 */
CREATE INDEX ix_assistant_draft_scope ON assistant_draft (tenant_id, org_id, is_deleted, updated_at);

/*
 * 当前的模型供应商或业务策略配置。settings_json 保存当前设置，revision 用于并发校验；历史配置另存于修订表。
 */
CREATE TABLE assistant_policy_record (
    id TEXT NOT NULL PRIMARY KEY,
    tenant_id TEXT NOT NULL,
    org_id TEXT NOT NULL,
    kind TEXT NOT NULL,
    name TEXT NOT NULL,
    business_code TEXT NULL,
    revision INTEGER NOT NULL,
    enabled INTEGER NOT NULL DEFAULT 1,
    settings_json TEXT NOT NULL,
    created_at TEXT NOT NULL,
    created_by TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    updated_by TEXT NOT NULL,
    is_deleted INTEGER NOT NULL DEFAULT 0,
    deleted_at TEXT NULL,
    deleted_by TEXT NULL
);
/*
 * 筛选组织范围内指定类型的当前模型或业务策略。
 * 适用表：assistant_policy_record。
 */
CREATE INDEX ix_assistant_policy_scope ON assistant_policy_record (tenant_id, org_id, kind, is_deleted);

/*
 * 模型供应商或业务策略的冻结历史修订。运行快照可按 record_id 和 revision 读取原始设置。
 */
CREATE TABLE assistant_policy_revision (
    id TEXT NOT NULL PRIMARY KEY,
    record_id TEXT NOT NULL,
    tenant_id TEXT NOT NULL,
    org_id TEXT NOT NULL,
    kind TEXT NOT NULL,
    revision INTEGER NOT NULL,
    enabled INTEGER NOT NULL,
    settings_json TEXT NOT NULL,
    created_at TEXT NOT NULL,
    created_by TEXT NOT NULL,
    is_deleted INTEGER NOT NULL DEFAULT 0,
    deleted_at TEXT NULL,
    deleted_by TEXT NULL
);
/*
 * 同一配置记录的每个修订号只能保存一次。
 * 适用表：assistant_policy_revision。
 */
CREATE UNIQUE INDEX uq_assistant_policy_revision ON assistant_policy_revision (record_id, revision);
/*
 * 按组织和配置类型查找未删除的历史修订。
 * 适用表：assistant_policy_revision。
 */
CREATE INDEX ix_assistant_policy_revision_scope ON assistant_policy_revision (tenant_id, org_id, kind, is_deleted);

/*
 * 不可变的助手发布版本。snapshot_json 保存发布时的配置，snapshot_hash 标识快照内容，assistant_key 用于公开访问。
 */
CREATE TABLE assistant_published_version (
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
    is_deleted INTEGER NOT NULL DEFAULT 0,
    deleted_at TEXT NULL,
    deleted_by TEXT NULL,
    published_at TEXT NOT NULL,
    published_by TEXT NOT NULL
);
/*
 * 同一助手在所属组织内的发布版本号唯一。
 * 适用表：assistant_published_version。
 */
CREATE UNIQUE INDEX uq_assistant_published_version_scope_no ON assistant_published_version (tenant_id, org_id, assistant_id, version_no);
/*
 * 按公开标识和当前范围查找未删除、未撤回的发布版本。
 * 适用表：assistant_published_version。
 */
CREATE INDEX ix_assistant_published_version_public ON assistant_published_version (assistant_key, tenant_id, org_id, is_revoked, is_deleted);
/*
 * 按助手标识和快照摘要定位相同发布内容。
 * 适用表：assistant_published_version。
 */
CREATE INDEX ix_assistant_published_version_hash ON assistant_published_version (assistant_id, snapshot_hash);

/*
 * 每个助手的当前发布指针。current_published_version_id 指向生效版本，pointer_revision 保护并发发布与撤回。
 */
CREATE TABLE assistant_publication_pointer (
    id TEXT NOT NULL PRIMARY KEY,
    assistant_key TEXT NOT NULL,
    assistant_id TEXT NOT NULL,
    tenant_id TEXT NOT NULL,
    org_id TEXT NOT NULL,
    current_published_version_id TEXT NULL,
    current_version_no INTEGER NOT NULL DEFAULT 0,
    pointer_revision INTEGER NOT NULL DEFAULT 0,
    status INTEGER NOT NULL DEFAULT 0,
    is_deleted INTEGER NOT NULL DEFAULT 0,
    deleted_at TEXT NULL,
    deleted_by TEXT NULL,
    created_at TEXT NOT NULL,
    created_by TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    updated_by TEXT NOT NULL
);
/*
 * 每个租户和组织中的助手只能有一个发布指针。
 * 适用表：assistant_publication_pointer。
 */
CREATE UNIQUE INDEX uq_assistant_publication_pointer_scope ON assistant_publication_pointer (tenant_id, org_id, assistant_id);
/*
 * 每个公开访问标识只能对应一个发布指针。
 * 适用表：assistant_publication_pointer。
 */
CREATE UNIQUE INDEX uq_assistant_publication_pointer_key ON assistant_publication_pointer (assistant_key);
/*
 * 按公开标识查找有效发布指针。
 * 适用表：assistant_publication_pointer。
 */
CREATE INDEX ix_assistant_publication_pointer_public ON assistant_publication_pointer (assistant_key, status, is_deleted);
/*
 * 通过发布版本标识反向查找当前指针。
 * 适用表：assistant_publication_pointer。
 */
CREATE INDEX ix_assistant_publication_pointer_version ON assistant_publication_pointer (current_published_version_id);

/*
 * 公开聊天的匿名会话。记录所属助手、租户、组织、固定发布版本、访问主体哈希、执行模式和有效期。
 */
CREATE TABLE assistant_anonymous_session (
    id TEXT NOT NULL PRIMARY KEY,
    assistant_id TEXT NOT NULL,
    tenant_id TEXT NOT NULL,
    org_id TEXT NOT NULL,
    published_version_id TEXT NOT NULL,
    subject_hash TEXT NOT NULL,
    locale TEXT NOT NULL,
    execution_mode INTEGER NOT NULL,
    status INTEGER NOT NULL,
    created_at TEXT NOT NULL,
    last_seen_at TEXT NOT NULL,
    expires_at TEXT NOT NULL,
    revoked_at TEXT NULL,
    revoked_by TEXT NULL,
    is_deleted INTEGER NOT NULL DEFAULT 0,
    deleted_at TEXT NULL,
    deleted_by TEXT NULL
);
/*
 * 按助手、组织、发布版本和执行模式筛选有效匿名会话。
 * 适用表：assistant_anonymous_session。
 */
CREATE INDEX ix_assistant_session_scope ON assistant_anonymous_session (assistant_id, tenant_id, org_id, published_version_id, execution_mode, is_deleted);
/*
 * 按助手及匿名主体摘要查询尚未软删除且未过期的会话。
 * 适用表：assistant_anonymous_session。
 */
CREATE INDEX ix_assistant_session_subject ON assistant_anonymous_session (assistant_id, subject_hash, is_deleted, expires_at);

/*
 * 匿名会话中的对话。保留固定发布和执行范围，last_turn_no 用于并发申请连续轮次编号。
 */
CREATE TABLE assistant_conversation (
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
    created_at TEXT NOT NULL,
    last_activity_at TEXT NOT NULL,
    is_deleted INTEGER NOT NULL DEFAULT 0,
    deleted_at TEXT NULL,
    deleted_by TEXT NULL
);
/*
 * 在助手、组织、发布版本和执行模式范围内筛选对话。
 * 适用表：assistant_conversation。
 */
CREATE INDEX ix_assistant_conversation_scope ON assistant_conversation (assistant_id, tenant_id, org_id, published_version_id, execution_mode, is_deleted);
/*
 * 按会话读取未删除对话，并按活动时间排序。
 * 适用表：assistant_conversation。
 */
CREATE INDEX ix_assistant_conversation_session ON assistant_conversation (session_id, is_deleted, last_activity_at);

/*
 * 对话中的一轮用户请求。client_message_id 和 request_hash 用于识别重试与内容冲突，turn_no 记录对话内顺序。
 */
CREATE TABLE assistant_turn (
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
    created_at TEXT NOT NULL,
    completed_at TEXT NULL,
    failure_code TEXT NULL,
    is_deleted INTEGER NOT NULL DEFAULT 0,
    deleted_at TEXT NULL,
    deleted_by TEXT NULL
);
/*
 * 同一对话中的客户端消息幂等标识唯一，避免重复提交生成多个轮次。
 * 适用表：assistant_turn。
 */
CREATE UNIQUE INDEX uq_assistant_turn_conversation_client_message ON assistant_turn (conversation_id, client_message_id);
/*
 * 按助手、组织、发布版本和执行模式筛选有效轮次。
 * 适用表：assistant_turn。
 */
CREATE INDEX ix_assistant_turn_scope ON assistant_turn (assistant_id, tenant_id, org_id, published_version_id, execution_mode, is_deleted);
/*
 * 按对话和轮次编号读取消息请求。
 * 适用表：assistant_turn。
 */
CREATE INDEX ix_assistant_turn_conversation_order ON assistant_turn (conversation_id, turn_no, is_deleted);

/*
 * 执行一轮助手回答的运行记录。保存模型修订、运行状态和事件序号，以便恢复流式结果。
 */
CREATE TABLE assistant_run (
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
    created_at TEXT NOT NULL,
    started_at TEXT NULL,
    completed_at TEXT NULL,
    failure_code TEXT NULL,
    prompt_tokens INTEGER NULL,
    completion_tokens INTEGER NULL,
    total_tokens INTEGER NULL,
    is_deleted INTEGER NOT NULL DEFAULT 0,
    deleted_at TEXT NULL,
    deleted_by TEXT NULL
);
/*
 * 每个轮次最多对应一次助手运行。
 * 适用表：assistant_run。
 */
CREATE UNIQUE INDEX uq_assistant_run_turn ON assistant_run (turn_id);
/*
 * 按助手和发布范围筛选有效运行记录。
 * 适用表：assistant_run。
 */
CREATE INDEX ix_assistant_run_scope ON assistant_run (assistant_id, tenant_id, org_id, published_version_id, execution_mode, is_deleted);
/*
 * 按对话和创建时间读取运行历史。
 * 适用表：assistant_run。
 */
CREATE INDEX ix_assistant_run_conversation ON assistant_run (conversation_id, created_at, is_deleted);

/*
 * 对话中的用户、助手或工具消息。content_json 保存正文及结构化内容；run_id 关联产生该消息的模型运行。
 */
CREATE TABLE assistant_message (
    id TEXT NOT NULL PRIMARY KEY,
    conversation_id TEXT NOT NULL,
    turn_id TEXT NOT NULL,
    run_id TEXT NULL,
    role INTEGER NOT NULL,
    status INTEGER NOT NULL,
    content_json TEXT NOT NULL,
    created_at TEXT NOT NULL,
    completed_at TEXT NULL,
    is_deleted INTEGER NOT NULL DEFAULT 0,
    deleted_at TEXT NULL,
    deleted_by TEXT NULL
);
/*
 * 按对话时间顺序查询有效消息。
 * 适用表：assistant_message。
 */
CREATE INDEX ix_assistant_message_conversation ON assistant_message (conversation_id, created_at, is_deleted);
/*
 * 读取指定轮次的消息。
 * 适用表：assistant_message。
 */
CREATE INDEX ix_assistant_message_turn ON assistant_message (turn_id, created_at, is_deleted);
/*
 * 读取指定运行产生的消息。
 * 适用表：assistant_message。
 */
CREATE INDEX ix_assistant_message_run ON assistant_message (run_id, created_at, is_deleted);

/*
 * 运行过程中提交的工具请求及结果。保存工具契约版本、输入摘要、执行状态和输出或错误码。
 */
CREATE TABLE assistant_tool_call (
    id TEXT NOT NULL PRIMARY KEY,
    run_id TEXT NOT NULL,
    tool_id TEXT NOT NULL,
    contract_version TEXT NOT NULL,
    input_hash TEXT NOT NULL,
    status INTEGER NOT NULL,
    output_json TEXT NULL,
    failure_code TEXT NULL,
    created_at TEXT NOT NULL,
    completed_at TEXT NULL,
    is_deleted INTEGER NOT NULL DEFAULT 0,
    deleted_at TEXT NULL,
    deleted_by TEXT NULL
);
/*
 * 按运行及创建顺序查询工具调用。
 * 适用表：assistant_tool_call。
 */
CREATE INDEX ix_assistant_tool_call_run ON assistant_tool_call (run_id, created_at, is_deleted);
/*
 * 按工具标识和契约版本查询有效调用。
 * 适用表：assistant_tool_call。
 */
CREATE INDEX ix_assistant_tool_call_tool ON assistant_tool_call (tool_id, contract_version, is_deleted);

/*
 * 组织范围内的知识库主记录及创建、修改和软删除审计信息。
 */
CREATE TABLE assistant_knowledge_base (
    id TEXT NOT NULL PRIMARY KEY,
    tenant_id TEXT NOT NULL,
    org_id TEXT NOT NULL,
    name TEXT NOT NULL,
    description TEXT NULL,
    is_deleted INTEGER NOT NULL DEFAULT 0,
    deleted_at TEXT NULL,
    deleted_by TEXT NULL,
    created_at TEXT NOT NULL,
    created_by TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    updated_by TEXT NOT NULL
);
/*
 * 按组织范围列出知识库，并使用修改时间排序。
 * 适用表：assistant_knowledge_base。
 */
CREATE INDEX ix_assistant_knowledge_base_scope ON assistant_knowledge_base (tenant_id, org_id, is_deleted, updated_at);

/*
 * 知识库的固定问答。normalized_question 用于匹配和唯一性约束，show_as_suggestion 控制公开推荐展示。
 */
CREATE TABLE assistant_knowledge_qna (
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
    is_deleted INTEGER NOT NULL DEFAULT 0,
    deleted_at TEXT NULL,
    deleted_by TEXT NULL,
    created_at TEXT NOT NULL,
    created_by TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    updated_by TEXT NOT NULL
);
/*
 * 同一知识库中的规范化问题与软删除状态组合唯一，保留原系统的历史墓碑约束。
 * 适用表：assistant_knowledge_qna。
 */
CREATE UNIQUE INDEX uq_assistant_knowledge_qna_exact ON assistant_knowledge_qna (knowledge_base_id, normalized_question, is_deleted);
/*
 * 按组织、知识库、启用和删除状态及问答排序查询。
 * 适用表：assistant_knowledge_qna。
 */
CREATE INDEX ix_assistant_knowledge_qna_scope ON assistant_knowledge_qna (tenant_id, org_id, knowledge_base_id, enabled, is_deleted, sort_order);

/*
 * 知识库文档主记录，与私有文件资产关联。current_version_no 指向最近处理版本，status 表示当前处理状态。
 */
CREATE TABLE assistant_knowledge_document (
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
    is_deleted INTEGER NOT NULL DEFAULT 0,
    deleted_at TEXT NULL,
    deleted_by TEXT NULL,
    created_at TEXT NOT NULL,
    created_by TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    updated_by TEXT NOT NULL
);
/*
 * 限制同一组织内文件资产与删除状态组合重复登记。
 * 适用表：assistant_knowledge_document。
 */
CREATE UNIQUE INDEX uq_assistant_knowledge_document_asset ON assistant_knowledge_document (tenant_id, org_id, asset_id, is_deleted);
/*
 * 按组织和知识库查询有效文档及最近修改状态。
 * 适用表：assistant_knowledge_document。
 */
CREATE INDEX ix_assistant_knowledge_document_scope ON assistant_knowledge_document (tenant_id, org_id, knowledge_base_id, is_deleted, updated_at);

/*
 * 文档解析后的不可变版本元数据。保存内容哈希、解析器版本、分块策略、片段数量和索引状态。
 */
CREATE TABLE assistant_knowledge_document_version (
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
    is_deleted INTEGER NOT NULL DEFAULT 0,
    deleted_at TEXT NULL,
    deleted_by TEXT NULL,
    created_at TEXT NOT NULL,
    created_by TEXT NOT NULL
);
/*
 * 同一文档版本号与删除状态组合唯一。
 * 适用表：assistant_knowledge_document_version。
 */
CREATE UNIQUE INDEX uq_assistant_knowledge_document_version ON assistant_knowledge_document_version (document_id, version_no, is_deleted);
/*
 * 按组织和文档读取各版本及其版本顺序。
 * 适用表：assistant_knowledge_document_version。
 */
CREATE INDEX ix_assistant_knowledge_document_version_scope ON assistant_knowledge_document_version (tenant_id, org_id, document_id, is_deleted, version_no);

/*
 * 一个文档版本中的文本片段。chunk_index 保存片段顺序，content_hash 验证正文，point_id 关联向量索引。
 */
CREATE TABLE assistant_knowledge_chunk (
    id TEXT NOT NULL PRIMARY KEY,
    tenant_id TEXT NOT NULL,
    org_id TEXT NOT NULL,
    document_version_id TEXT NOT NULL,
    chunk_index INTEGER NOT NULL,
    content_hash TEXT NOT NULL,
    content TEXT NOT NULL,
    token_count INTEGER NOT NULL DEFAULT 0,
    point_id TEXT NULL,
    is_deleted INTEGER NOT NULL DEFAULT 0,
    deleted_at TEXT NULL,
    deleted_by TEXT NULL,
    created_at TEXT NOT NULL,
    created_by TEXT NOT NULL
);
/*
 * 同一文档版本中片段顺序与删除状态组合唯一。
 * 适用表：assistant_knowledge_chunk。
 */
CREATE UNIQUE INDEX uq_assistant_knowledge_chunk_index ON assistant_knowledge_chunk (document_version_id, chunk_index, is_deleted);
/*
 * 按组织和文档版本读取有效文本片段及其顺序。
 * 适用表：assistant_knowledge_chunk。
 */
CREATE INDEX ix_assistant_knowledge_chunk_scope ON assistant_knowledge_chunk (tenant_id, org_id, document_version_id, is_deleted, chunk_index);

/*
 * 知识库当前分块策略及修订号。大小、重叠、标题路径和短片段合并设置在此生效。
 */
CREATE TABLE assistant_knowledge_chunking_policy (
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
    is_deleted INTEGER NOT NULL DEFAULT 0,
    deleted_at TEXT NULL,
    deleted_by TEXT NULL,
    created_at TEXT NOT NULL,
    created_by TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    updated_by TEXT NOT NULL
);
/*
 * 同一知识库和组织中只能有一条有效策略。
 * 适用表：assistant_knowledge_chunking_policy。
 */
CREATE UNIQUE INDEX uq_assistant_knowledge_chunking_policy_scope ON assistant_knowledge_chunking_policy (tenant_id, org_id, knowledge_base_id, is_deleted);
/*
 * 按组织及知识库读取分块策略修订。
 * 适用表：assistant_knowledge_chunking_policy。
 */
CREATE INDEX ix_assistant_knowledge_chunking_policy_scope ON assistant_knowledge_chunking_policy (tenant_id, org_id, knowledge_base_id, is_deleted, revision);

/*
 * 处理单个文档版本的向量索引任务。generation、嵌入模型和分块策略构成任务开始时冻结的配置清单。
 */
CREATE TABLE assistant_knowledge_index_job (
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
    created_at TEXT NOT NULL,
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
    content_hash TEXT NULL
);
/*
 * 按组织、知识库、任务状态及创建时间列出索引任务。
 * 适用表：assistant_knowledge_index_job。
 */
CREATE INDEX ix_assistant_knowledge_index_job_scope ON assistant_knowledge_index_job (tenant_id, org_id, knowledge_base_id, status, created_at);
/*
 * 按文档版本及创建时间查询相关索引任务。
 * 适用表：assistant_knowledge_index_job。
 */
CREATE INDEX ix_assistant_knowledge_index_job_version ON assistant_knowledge_index_job (document_version_id, created_at);

/*
 * 绑定操作人和草稿修订号的预览快照。保存独立预览会话、对话标识及过期时间。
 */
CREATE TABLE assistant_preview_snapshot (
    id TEXT NOT NULL PRIMARY KEY,
 tenant_id TEXT NOT NULL, org_id TEXT NOT NULL,
 assistant_id TEXT NOT NULL, actor_id TEXT NOT NULL,
 draft_revision INTEGER NOT NULL, configuration_json TEXT NOT NULL,
 session_id TEXT NOT NULL, conversation_id TEXT NOT NULL,
 created_at TEXT NOT NULL, expires_at TEXT NOT NULL,
 is_deleted INTEGER NOT NULL DEFAULT 0,
 deleted_at TEXT NULL, deleted_by TEXT NULL
);
/*
 * 按操作人与助手范围查询未删除的草稿预览。
 * 适用表：assistant_preview_snapshot。
 */
CREATE INDEX ix_assistant_preview_scope ON assistant_preview_snapshot (tenant_id, org_id, assistant_id, actor_id, is_deleted);

/*
 * 知识库目标嵌入配置及已经就绪的生效配置。target_generation 和 active_generation 分离，避免未完成重建影响现有检索。
 */
CREATE TABLE assistant_knowledge_embedding_policy (
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
    is_deleted               INTEGER NOT NULL DEFAULT 0,
    deleted_at               TEXT NULL,
    deleted_by               TEXT NULL,
    created_at               TEXT NOT NULL,
    created_by               TEXT NOT NULL,
    updated_at               TEXT NOT NULL,
    updated_by               TEXT NOT NULL,
    PRIMARY KEY (id)
);
/*
 * 同一知识库和组织中只能有一条有效嵌入配置。
 * 适用表：assistant_knowledge_embedding_policy。
 */
CREATE UNIQUE INDEX uq_assistant_knowledge_embedding_policy_scope ON assistant_knowledge_embedding_policy (tenant_id, org_id, knowledge_base_id, is_deleted);
/*
 * 按组织与知识库查询嵌入配置及其修订。
 * 适用表：assistant_knowledge_embedding_policy。
 */
CREATE INDEX ix_assistant_knowledge_embedding_policy_scope ON assistant_knowledge_embedding_policy (tenant_id, org_id, knowledge_base_id, is_deleted, revision);

/*
 * 定位指定嵌入代次和任务状态，追踪索引重建进度。
 * 适用表：assistant_knowledge_index_job。
 */
CREATE INDEX ix_assistant_knowledge_index_job_generation ON assistant_knowledge_index_job (tenant_id, org_id, knowledge_base_id, generation, status);

/*
 * 随业务变更写入本地事务的领域事件记录，保存事件类型、JSON 负载和创建时间。
 */
CREATE TABLE assistant_outbox (
    id TEXT NOT NULL PRIMARY KEY,
    event_type TEXT NOT NULL,
    payload_json TEXT NOT NULL,
    created_at TEXT NOT NULL
);
/*
 * 按创建时间扫描本地领域事件记录。
 * 适用表：assistant_outbox。
 */
CREATE INDEX ix_assistant_outbox_created ON assistant_outbox(created_at);

/*
 * 持久化向量集合及固定向量维度；维度约束确保集合内向量可以进行相似度计算。
 */
CREATE TABLE assistant_vector_collection (
    name TEXT NOT NULL PRIMARY KEY,
    dimension INTEGER NOT NULL CHECK (dimension > 0)
);
/*
 * 向量集合中的文档片段。联合主键限制集合内片段唯一，并通过外键保证所属集合存在。
 */
CREATE TABLE assistant_vector_point (
    collection_name TEXT NOT NULL,
    chunk_id TEXT NOT NULL,
    content_hash TEXT NOT NULL,
    document_id TEXT NOT NULL,
    version_no INTEGER NOT NULL,
    chunk_index INTEGER NOT NULL,
    vector_json TEXT NOT NULL,
    PRIMARY KEY (collection_name, chunk_id),
    FOREIGN KEY (collection_name) REFERENCES assistant_vector_collection(name)
);
