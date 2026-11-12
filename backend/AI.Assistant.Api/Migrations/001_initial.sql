/*
 * 开发阶段的当前 SQLite 初始化结构，直接创建业务表、索引及删除级联约束。
 * 结构变更后重建开发数据库，不提供历史版本升级。
 */

/*
 * 公开聊天和草稿预览使用的匿名会话，保存访问范围及有效期。
 */

CREATE TABLE assistant_anonymous_session (
    /*
     * 记录当前数据的唯一标识。
     */
    id TEXT NOT NULL PRIMARY KEY,

    /*
     * 关联此数据所属的助手。
     */
    assistant_id TEXT NOT NULL,

    /*
     * 绑定创建此会话或运行时使用的固定发布版本。
     */
    published_version_id TEXT NOT NULL,

    /*
     * 保存会话主体标识的摘要。
     */
    subject_hash TEXT NOT NULL,

    /*
     * 记录会话或请求使用的语言区域。
     */
    locale TEXT NOT NULL,

    /*
     * 区分公开执行和管理端预览执行。
     */
    execution_mode INTEGER NOT NULL,

    /*
     * 记录当前生命周期状态。
     */
    status INTEGER NOT NULL,

    /*
     * 记录匿名会话最近一次访问的时间。
     */
    last_seen_at TEXT NOT NULL,

    /*
     * 记录会话、票据或预览失效时间。
     */
    expires_at TEXT NOT NULL,

    /*
     * 记录授权或版本撤回的时间。
     */
    revoked_at TEXT NULL
);

/*
 * 加速按 assistant_id, published_version_id, execution_mode 查询。
 */

CREATE INDEX ix_assistant_session_scope ON assistant_anonymous_session (
    assistant_id,
    published_version_id,
    execution_mode
);

/*
 * 加速按 assistant_id, subject_hash, expires_at 查询。
 */

CREATE INDEX ix_assistant_session_subject ON assistant_anonymous_session (
    assistant_id,
    subject_hash,
    expires_at
);

/*
 * 匿名会话中的对话，保存语言、运行模式和最近活动时间。
 */

CREATE TABLE assistant_conversation (
    /*
     * 记录当前数据的唯一标识。
     */
    id TEXT NOT NULL PRIMARY KEY,

    /*
     * 关联创建该对话或请求的匿名会话。
     */
    session_id TEXT NOT NULL,

    /*
     * 关联此数据所属的助手。
     */
    assistant_id TEXT NOT NULL,

    /*
     * 绑定创建此会话或运行时使用的固定发布版本。
     */
    published_version_id TEXT NOT NULL,

    /*
     * 记录会话或请求使用的语言区域。
     */
    locale TEXT NOT NULL,

    /*
     * 区分公开执行和管理端预览执行。
     */
    execution_mode INTEGER NOT NULL,

    /*
     * 记录当前生命周期状态。
     */
    status INTEGER NOT NULL,

    /*
     * 记录当前对话已分配的最后轮次号。
     */
    last_turn_no INTEGER NOT NULL,

    /*
     * 记录对话最近一次活动的时间。
     */
    last_activity_at TEXT NOT NULL,

    /*
     * 关联 assistant_anonymous_session，父记录删除时级联清理当前记录。
     */
    FOREIGN KEY (session_id) REFERENCES assistant_anonymous_session(id) ON DELETE CASCADE
);

/*
 * 加速按 assistant_id, published_version_id, execution_mode 查询。
 */

CREATE INDEX ix_assistant_conversation_scope ON assistant_conversation (
    assistant_id,
    published_version_id,
    execution_mode
);

/*
 * 加速按 session_id, last_activity_at 查询。
 */

CREATE INDEX ix_assistant_conversation_session ON assistant_conversation (
    session_id,
    last_activity_at
);

/*
 * 助手可编辑草稿，保存配置、修订号及创建幂等键。
 */

CREATE TABLE assistant_draft (
    /*
     * 记录当前数据的唯一标识。
     */
    id TEXT NOT NULL PRIMARY KEY,

    /*
     * 记录此资源的展示名称。
     */
    name TEXT NOT NULL,

    /*
     * 记录此资源的可选说明。
     */
    description TEXT NULL,

    /*
     * 保存尚未发布的可编辑助手配置。
     */
    draft_json TEXT NOT NULL,

    /*
     * 记录草稿修订号，用于检测并发保存。
     */
    draft_revision INTEGER NOT NULL,

    /*
     * 保存创建请求的幂等键，用于识别重复提交。
     */
    creation_idempotency_key TEXT NULL
);

/*
 * 保证 creation_idempotency_key 的组合唯一。
 */

CREATE UNIQUE INDEX uq_assistant_draft_scope_idempotency ON assistant_draft (
    creation_idempotency_key
);

/*
 * 知识库基本信息，文档、固定问答和索引配置以此为入口。
 */

CREATE TABLE assistant_knowledge_base (
    /*
     * 记录当前数据的唯一标识。
     */
    id TEXT NOT NULL PRIMARY KEY,

    /*
     * 记录此资源的展示名称。
     */
    name TEXT NOT NULL,

    /*
     * 记录此资源的可选说明。
     */
    description TEXT NULL
);

/*
 * 文档版本的可检索片段，保存内容、位置及索引状态。
 */

CREATE TABLE assistant_knowledge_chunk (
    /*
     * 记录当前数据的唯一标识。
     */
    id TEXT NOT NULL PRIMARY KEY,

    /*
     * 关联当前片段或索引任务使用的文档版本。
     */
    document_version_id TEXT NOT NULL,

    /*
     * 记录片段在所属文档中的顺序。
     */
    chunk_index INTEGER NOT NULL,

    /*
     * 记录正文的内容摘要，用于检测内容变化。
     */
    content_hash TEXT NOT NULL,

    /*
     * 映射数据库中的“内容”列，并由仓储负责读取和持久化。
     */
    content TEXT NOT NULL,

    /*
     * 记录片段或内容估算的令牌数量。
     */
    token_count INTEGER NOT NULL DEFAULT 0,

    /*
     * 记录外部向量索引中对应点的标识。
     */
    point_id TEXT NULL,

    /*
     * 关联 assistant_knowledge_document_version，父记录删除时级联清理当前记录。
     */
    FOREIGN KEY (document_version_id) REFERENCES assistant_knowledge_document_version(id) ON DELETE CASCADE
);

/*
 * 加速按 document_version_id, chunk_index 查询。
 */

CREATE INDEX ix_assistant_knowledge_chunk_scope ON assistant_knowledge_chunk (
    document_version_id,
    chunk_index
);

/*
 * 保证 document_version_id, chunk_index 的组合唯一。
 */

CREATE UNIQUE INDEX uq_assistant_knowledge_chunk_index ON assistant_knowledge_chunk (
    document_version_id,
    chunk_index
);

/*
 * 知识库分块策略，保存切分参数和策略修订号。
 */

CREATE TABLE assistant_knowledge_chunking_policy (
    /*
     * 记录当前数据的唯一标识。
     */
    id TEXT NOT NULL PRIMARY KEY,

    /*
     * 关联此数据所属的知识库。
     */
    knowledge_base_id TEXT NOT NULL,

    /*
     * 映射数据库中的“模式”列，并由仓储负责读取和持久化。
     */
    mode TEXT NOT NULL DEFAULT 'hybrid',

    /*
     * 限制单个文本片段的最大字符数。
     */
    max_chunk_size INTEGER NOT NULL DEFAULT 1200,

    /*
     * 指定短片段合并所依据的最小字符数。
     */
    min_chunk_size INTEGER NOT NULL DEFAULT 200,

    /*
     * 指定相邻文本片段保留的重叠字符数。
     */
    overlap_size INTEGER NOT NULL DEFAULT 160,

    /*
     * 限制用于文档分段的 Markdown 标题层级。
     */
    max_heading_level INTEGER NOT NULL DEFAULT 3,

    /*
     * 控制片段正文是否保留所属标题路径。
     */
    preserve_heading_path INTEGER NOT NULL DEFAULT 1,

    /*
     * 控制是否合并长度不足的相邻片段。
     */
    merge_short_chunks INTEGER NOT NULL DEFAULT 1,

    /*
     * 记录当前配置修订号，用于乐观并发控制。
     */
    revision INTEGER NOT NULL DEFAULT 1,

    /*
     * 关联 assistant_knowledge_base，父记录删除时级联清理当前记录。
     */
    FOREIGN KEY (knowledge_base_id) REFERENCES assistant_knowledge_base(id) ON DELETE CASCADE
);

/*
 * 加速按 knowledge_base_id, revision 查询。
 */

CREATE INDEX ix_assistant_knowledge_chunking_policy_scope ON assistant_knowledge_chunking_policy (
    knowledge_base_id,
    revision
);

/*
 * 保证 knowledge_base_id 的组合唯一。
 */

CREATE UNIQUE INDEX uq_assistant_knowledge_chunking_policy_scope ON assistant_knowledge_chunking_policy (
    knowledge_base_id
);

/*
 * 知识库文档及文件资产关联，保存当前版本和处理状态。
 */

CREATE TABLE assistant_knowledge_document (
    /*
     * 记录当前数据的唯一标识。
     */
    id TEXT NOT NULL PRIMARY KEY,

    /*
     * 关联此数据所属的知识库。
     */
    knowledge_base_id TEXT NOT NULL,

    /*
     * 映射数据库中的“资源”列，并由仓储负责读取和持久化。
     */
    asset_id TEXT NOT NULL,

    /*
     * 记录上传文件的原始文件名。
     */
    file_name TEXT NOT NULL,

    /*
     * 记录上传文件的媒体类型。
     */
    content_type TEXT NOT NULL,

    /*
     * 记录文件占用的字节数。
     */
    size_bytes INTEGER NOT NULL,

    /*
     * 记录当前发布版本号。
     */
    current_version_no INTEGER NOT NULL DEFAULT 0,

    /*
     * 记录当前生命周期状态。
     */
    status TEXT NOT NULL DEFAULT 'uploaded',

    /*
     * 保存任务最近一次失败的稳定错误码。
     */
    last_error_code TEXT NULL,

    /*
     * 关联 assistant_knowledge_base，父记录删除时级联清理当前记录。
     */
    FOREIGN KEY (knowledge_base_id) REFERENCES assistant_knowledge_base(id) ON DELETE CASCADE
);

/*
 * 加速按 knowledge_base_id 查询。
 */

CREATE INDEX ix_assistant_knowledge_document_scope ON assistant_knowledge_document (
    knowledge_base_id
);

/*
 * 保证 asset_id 的组合唯一。
 */

CREATE UNIQUE INDEX uq_assistant_knowledge_document_asset ON assistant_knowledge_document (
    asset_id
);

/*
 * 文档的不可变版本，保存解析内容、分块策略和索引状态。
 */

CREATE TABLE assistant_knowledge_document_version (
    /*
     * 记录当前数据的唯一标识。
     */
    id TEXT NOT NULL PRIMARY KEY,

    /*
     * 关联产生此数据的原始文档。
     */
    document_id TEXT NOT NULL,

    /*
     * 记录发布版本或文档版本的递增编号。
     */
    version_no INTEGER NOT NULL,

    /*
     * 记录正文的内容摘要，用于检测内容变化。
     */
    content_hash TEXT NOT NULL,

    /*
     * 记录生成当前文档正文使用的解析器版本。
     */
    parser_version TEXT NOT NULL,

    /*
     * 记录生成当前片段使用的策略标识。
     */
    chunk_strategy TEXT NOT NULL,

    /*
     * 保存文档处理时使用的分块策略 JSON。
     */
    chunk_policy_json TEXT NOT NULL,

    /*
     * 记录当前生命周期状态。
     */
    status TEXT NOT NULL DEFAULT 'uploaded',

    /*
     * 记录文档生成的文本片段数量。
     */
    chunk_count INTEGER NOT NULL DEFAULT 0,

    /*
     * 映射数据库中的“索引状态”列，并由仓储负责读取和持久化。
     */
    index_status TEXT NOT NULL DEFAULT 'queued',

    /*
     * 记录文档版本完成索引的时间。
     */
    indexed_at TEXT NULL,

    /*
     * 保存可稳定识别的错误代码。
     */
    error_code TEXT NULL,

    /*
     * 关联 assistant_knowledge_document，父记录删除时级联清理当前记录。
     */
    FOREIGN KEY (document_id) REFERENCES assistant_knowledge_document(id) ON DELETE CASCADE
);

/*
 * 加速按 document_id, version_no 查询。
 */

CREATE INDEX ix_assistant_knowledge_document_version_scope ON assistant_knowledge_document_version (
    document_id,
    version_no
);

/*
 * 保证 document_id, version_no 的组合唯一。
 */

CREATE UNIQUE INDEX uq_assistant_knowledge_document_version ON assistant_knowledge_document_version (
    document_id,
    version_no
);

/*
 * 知识库嵌入配置，区分目标配置与当前生效配置。
 */

CREATE TABLE assistant_knowledge_embedding_policy (
    /*
     * 记录当前数据的唯一标识。
     */
    id                       TEXT NOT NULL,

    /*
     * 关联此数据所属的知识库。
     */
    knowledge_base_id        TEXT NOT NULL,

    /*
     * 记录使用的模型或嵌入供应商标识。
     */
    provider_id              TEXT NOT NULL,

    /*
     * 绑定使用的供应商配置修订。
     */
    provider_revision        INTEGER NOT NULL,

    /*
     * 记录当前选择的模型标识。
     */
    model                    TEXT NOT NULL,

    /*
     * 记录嵌入向量的固定维度。
     */
    dimension                INTEGER NOT NULL,

    /*
     * 正在构建或计划构建的索引代次，与已生效代次分别维护。
     */
    target_generation        INTEGER NOT NULL,

    /*
     * 映射数据库中的“生效模型供应商”列，并由仓储负责读取和持久化。
     */
    active_provider_id       TEXT NULL,

    /*
     * 映射数据库中的“生效模型供应商修订”列，并由仓储负责读取和持久化。
     */
    active_provider_revision INTEGER NULL,

    /*
     * 映射数据库中的“生效模型”列，并由仓储负责读取和持久化。
     */
    active_model             TEXT NULL,

    /*
     * 映射数据库中的“生效维度”列，并由仓储负责读取和持久化。
     */
    active_dimension         INTEGER NULL,

    /*
     * 已完成索引并可供发布快照引用的生效代次。
     */
    active_generation        INTEGER NOT NULL DEFAULT 0,

    /*
     * 映射数据库中的“生效配置修订”列，并由仓储负责读取和持久化。
     */
    active_profile_revision  INTEGER NULL,

    /*
     * 记录当前生命周期状态。
     */
    status                   TEXT NOT NULL DEFAULT 'rebuilding',

    /*
     * 保存任务最近一次失败的稳定错误码。
     */
    last_error_code          TEXT NULL,

    /*
     * 记录当前配置修订号，用于乐观并发控制。
     */
    revision                 INTEGER NOT NULL DEFAULT 1,

    /*
     * 关联 assistant_knowledge_base，父记录删除时级联清理当前记录。
     */
    FOREIGN KEY (knowledge_base_id) REFERENCES assistant_knowledge_base(id) ON DELETE CASCADE
);

/*
 * 加速按 knowledge_base_id, revision 查询。
 */

CREATE INDEX ix_assistant_knowledge_embedding_policy_scope ON assistant_knowledge_embedding_policy (
    knowledge_base_id,
    revision
);

/*
 * 保证 knowledge_base_id 的组合唯一。
 */

CREATE UNIQUE INDEX uq_assistant_knowledge_embedding_policy_scope ON assistant_knowledge_embedding_policy (
    knowledge_base_id
);

/*
 * 知识文档索引任务，保存冻结配置、处理进度和完成结果。
 */

CREATE TABLE assistant_knowledge_index_job (
    /*
     * 记录当前数据的唯一标识。
     */
    id TEXT NOT NULL PRIMARY KEY,

    /*
     * 关联此数据所属的知识库。
     */
    knowledge_base_id TEXT NOT NULL,

    /*
     * 关联产生此数据的原始文档。
     */
    document_id TEXT NOT NULL,

    /*
     * 关联当前片段或索引任务使用的文档版本。
     */
    document_version_id TEXT NOT NULL,

    /*
     * 记录发布版本或文档版本的递增编号。
     */
    version_no INTEGER NOT NULL,

    /*
     * 记录当前生命周期状态。
     */
    status TEXT NOT NULL DEFAULT 'queued',

    /*
     * 记录任务已经尝试执行的次数。
     */
    attempt_count INTEGER NOT NULL DEFAULT 0,

    /*
     * 记录文档生成的文本片段数量。
     */
    chunk_count INTEGER NOT NULL DEFAULT 0,

    /*
     * 保存可稳定识别的错误代码。
     */
    error_code TEXT NULL,

    /*
     * 记录执行或索引任务的开始时间。
     */
    started_at TEXT NULL,

    /*
     * 记录处理完成或进入终态的时间。
     */
    completed_at TEXT NULL,

    /*
     * 记录当前索引或配置代次，用于隔离重建任务。
     */
    generation INTEGER NULL,

    /*
     * 映射数据库中的“嵌入模型供应商”列，并由仓储负责读取和持久化。
     */
    embedding_provider_id TEXT NULL,

    /*
     * 映射数据库中的“嵌入模型供应商修订”列，并由仓储负责读取和持久化。
     */
    embedding_provider_revision INTEGER NULL,

    /*
     * 映射数据库中的“嵌入模型”列，并由仓储负责读取和持久化。
     */
    embedding_model TEXT NULL,

    /*
     * 映射数据库中的“嵌入维度”列，并由仓储负责读取和持久化。
     */
    embedding_dimension INTEGER NULL,

    /*
     * 记录生成片段时使用的分块策略修订。
     */
    chunk_policy_revision INTEGER NULL,

    /*
     * 保存文档处理时使用的分块策略 JSON。
     */
    chunk_policy_json TEXT NULL,

    /*
     * 记录生成当前文档正文使用的解析器版本。
     */
    parser_version TEXT NULL,

    /*
     * 记录正文的内容摘要，用于检测内容变化。
     */
    content_hash TEXT NULL,

    /*
     * 关联 assistant_knowledge_document_version，父记录删除时级联清理当前记录。
     */
    FOREIGN KEY (document_version_id) REFERENCES assistant_knowledge_document_version(id) ON DELETE CASCADE
);

/*
 * 加速按 knowledge_base_id, generation, status 查询。
 */

CREATE INDEX ix_assistant_knowledge_index_job_generation ON assistant_knowledge_index_job (
    knowledge_base_id,
    generation,
    status
);

/*
 * 加速按 knowledge_base_id, status 查询。
 */

CREATE INDEX ix_assistant_knowledge_index_job_scope ON assistant_knowledge_index_job (
    knowledge_base_id,
    status
);

/*
 * 加速按 document_version_id 查询。
 */

CREATE INDEX ix_assistant_knowledge_index_job_version ON assistant_knowledge_index_job (
    document_version_id
);

/*
 * 知识库固定问答，按标准化问题保证同一知识库内唯一。
 */

CREATE TABLE assistant_knowledge_qna (
    /*
     * 记录当前数据的唯一标识。
     */
    id TEXT NOT NULL PRIMARY KEY,

    /*
     * 关联此数据所属的知识库。
     */
    knowledge_base_id TEXT NOT NULL,

    /*
     * 映射数据库中的“问题”列，并由仓储负责读取和持久化。
     */
    question TEXT NOT NULL,

    /*
     * 保存规范化后的问题文本，用于固定问答匹配和重复检测。
     */
    normalized_question TEXT NOT NULL,

    /*
     * 映射数据库中的“回答”列，并由仓储负责读取和持久化。
     */
    answer TEXT NULL,

    /*
     * 标记对应能力或配置是否启用。
     */
    enabled INTEGER NOT NULL DEFAULT 1,

    /*
     * 记录问答项在列表中的显示顺序。
     */
    sort_order INTEGER NOT NULL DEFAULT 0,

    /*
     * 标记该问答是否可以作为建议问题展示。
     */
    show_as_suggestion INTEGER NOT NULL DEFAULT 0,

    /*
     * 关联 assistant_knowledge_base，父记录删除时级联清理当前记录。
     */
    FOREIGN KEY (knowledge_base_id) REFERENCES assistant_knowledge_base(id) ON DELETE CASCADE
);

/*
 * 加速按 knowledge_base_id, enabled, sort_order 查询。
 */

CREATE INDEX ix_assistant_knowledge_qna_scope ON assistant_knowledge_qna (
    knowledge_base_id,
    enabled,
    sort_order
);

/*
 * 保证 knowledge_base_id, normalized_question 的组合唯一。
 */

CREATE UNIQUE INDEX uq_assistant_knowledge_qna_exact ON assistant_knowledge_qna (
    knowledge_base_id,
    normalized_question
);

/*
 * 对话消息及内容，关联所属轮次和执行运行。
 */

CREATE TABLE assistant_message (
    /*
     * 记录当前数据的唯一标识。
     */
    id TEXT NOT NULL PRIMARY KEY,

    /*
     * 关联此消息、轮次或运行所属的对话。
     */
    conversation_id TEXT NOT NULL,

    /*
     * 关联此记录所属的用户请求轮次。
     */
    turn_id TEXT NOT NULL,

    /*
     * 关联产生此事件、消息或工具调用的执行运行。
     */
    run_id TEXT NULL,

    /*
     * 区分用户、助手和工具消息。
     */
    role INTEGER NOT NULL,

    /*
     * 记录当前生命周期状态。
     */
    status INTEGER NOT NULL,

    /*
     * 保存消息正文及其附加字段的 JSON 内容。
     */
    content_json TEXT NOT NULL,

    /*
     * 记录处理完成或进入终态的时间。
     */
    completed_at TEXT NULL,

    /*
     * 关联 assistant_conversation，父记录删除时级联清理当前记录。
     */
    FOREIGN KEY (conversation_id) REFERENCES assistant_conversation(id) ON DELETE CASCADE,

    /*
     * 关联 assistant_turn，父记录删除时级联清理当前记录。
     */
    FOREIGN KEY (turn_id) REFERENCES assistant_turn(id) ON DELETE CASCADE,

    /*
     * 关联 assistant_run，父记录删除时级联清理当前记录。
     */
    FOREIGN KEY (run_id) REFERENCES assistant_run(id) ON DELETE CASCADE
);

/*
 * 加速按 conversation_id 查询。
 */

CREATE INDEX ix_assistant_message_conversation ON assistant_message (
    conversation_id
);

/*
 * 加速按 run_id 查询。
 */

CREATE INDEX ix_assistant_message_run ON assistant_message (
    run_id
);

/*
 * 加速按 turn_id 查询。
 */

CREATE INDEX ix_assistant_message_turn ON assistant_message (
    turn_id
);

/*
 * 本地领域事件发件箱，与业务写入使用同一事务提交。
 */

CREATE TABLE assistant_outbox (
    /*
     * 记录当前数据的唯一标识。
     */
    id TEXT NOT NULL PRIMARY KEY,

    /*
     * 表示本地事件发件箱事件的事件类型信息。
     */
    event_type TEXT NOT NULL,

    /*
     * 保存事件负载的序列化 JSON 内容。
     */
    payload_json TEXT NOT NULL
);

/*
 * 当前模型供应商或业务策略配置，修订号用于并发校验。
 */

CREATE TABLE assistant_policy_record (
    /*
     * 记录当前数据的唯一标识。
     */
    id TEXT NOT NULL PRIMARY KEY,

    /*
     * 表示助手策略记录的种类信息。
     */
    kind TEXT NOT NULL,

    /*
     * 记录此资源的展示名称。
     */
    name TEXT NOT NULL,

    /*
     * 保存统一业务响应的结果代码。
     */
    business_code TEXT NULL,

    /*
     * 记录当前配置修订号，用于乐观并发控制。
     */
    revision INTEGER NOT NULL,

    /*
     * 标记对应能力或配置是否启用。
     */
    enabled INTEGER NOT NULL DEFAULT 1,

    /*
     * 保存版本化模型或业务策略设置。
     */
    settings_json TEXT NOT NULL
);

/*
 * 加速按 kind 查询。
 */

CREATE INDEX ix_assistant_policy_scope ON assistant_policy_record (
    kind
);

/*
 * 模型供应商和业务策略的冻结修订，供执行快照引用。
 */

CREATE TABLE assistant_policy_revision (
    /*
     * 记录当前数据的唯一标识。
     */
    id TEXT NOT NULL PRIMARY KEY,

    /*
     * 表示助手策略修订记录的记录信息。
     */
    record_id TEXT NOT NULL,

    /*
     * 表示助手策略修订记录的种类信息。
     */
    kind TEXT NOT NULL,

    /*
     * 记录当前配置修订号，用于乐观并发控制。
     */
    revision INTEGER NOT NULL,

    /*
     * 标记对应能力或配置是否启用。
     */
    enabled INTEGER NOT NULL,

    /*
     * 保存版本化模型或业务策略设置。
     */
    settings_json TEXT NOT NULL,

    /*
     * 关联 assistant_policy_record，父记录删除时级联清理当前记录。
     */
    FOREIGN KEY (record_id) REFERENCES assistant_policy_record(id) ON DELETE CASCADE
);

/*
 * 加速按 kind 查询。
 */

CREATE INDEX ix_assistant_policy_revision_scope ON assistant_policy_revision (
    kind
);

/*
 * 保证 record_id, revision 的组合唯一。
 */

CREATE UNIQUE INDEX uq_assistant_policy_revision ON assistant_policy_revision (
    record_id,
    revision
);

/*
 * 草稿预览执行快照，绑定匿名会话、对话及失效时间。
 */

CREATE TABLE assistant_preview_snapshot (
    /*
     * 记录当前数据的唯一标识。
     */
    id TEXT NOT NULL PRIMARY KEY,

    /*
     * 关联此数据所属的助手。
     */
    assistant_id TEXT NOT NULL,

    /*
     * 记录草稿修订号，用于检测并发保存。
     */
    draft_revision INTEGER NOT NULL,

    /*
     * 保存序列化的助手配置或执行快照。
     */
    configuration_json TEXT NOT NULL,

    /*
     * 关联创建该对话或请求的匿名会话。
     */
    session_id TEXT NOT NULL,

    /*
     * 关联此消息、轮次或运行所属的对话。
     */
    conversation_id TEXT NOT NULL,

    /*
     * 记录会话、票据或预览失效时间。
     */
    expires_at TEXT NOT NULL
);

/*
 * 加速按 assistant_id 查询。
 */

CREATE INDEX ix_assistant_preview_scope ON assistant_preview_snapshot (
    assistant_id
);

/*
 * 助手当前发布指针，通过指针修订号保护并发发布与撤回。
 */

CREATE TABLE assistant_publication_pointer (
    /*
     * 记录当前数据的唯一标识。
     */
    id TEXT NOT NULL PRIMARY KEY,

    /*
     * 映射数据库中的“助手密钥”列，并由仓储负责读取和持久化。
     */
    assistant_key TEXT NOT NULL,

    /*
     * 关联此数据所属的助手。
     */
    assistant_id TEXT NOT NULL,

    /*
     * 指向助手当前生效的发布版本。
     */
    current_published_version_id TEXT NULL,

    /*
     * 记录当前发布版本号。
     */
    current_version_no INTEGER NOT NULL DEFAULT 0,

    /*
     * 记录发布指针修订号，用于检测并发发布或撤回。
     */
    pointer_revision INTEGER NOT NULL DEFAULT 0,

    /*
     * 记录当前生命周期状态。
     */
    status INTEGER NOT NULL DEFAULT 0
);

/*
 * 加速按 assistant_key, status 查询。
 */

CREATE INDEX ix_assistant_publication_pointer_public ON assistant_publication_pointer (
    assistant_key,
    status
);

/*
 * 加速按 current_published_version_id 查询。
 */

CREATE INDEX ix_assistant_publication_pointer_version ON assistant_publication_pointer (
    current_published_version_id
);

/*
 * 保证 assistant_key 的组合唯一。
 */

CREATE UNIQUE INDEX uq_assistant_publication_pointer_key ON assistant_publication_pointer (
    assistant_key
);

/*
 * 保证 assistant_id 的组合唯一。
 */

CREATE UNIQUE INDEX uq_assistant_publication_pointer_scope ON assistant_publication_pointer (
    assistant_id
);

/*
 * 不可变的助手发布版本，保存发布快照、摘要及发布和撤回时间。
 */

CREATE TABLE assistant_published_version (
    /*
     * 记录当前数据的唯一标识。
     */
    id TEXT NOT NULL PRIMARY KEY,

    /*
     * 映射数据库中的“助手密钥”列，并由仓储负责读取和持久化。
     */
    assistant_key TEXT NOT NULL,

    /*
     * 关联此数据所属的助手。
     */
    assistant_id TEXT NOT NULL,

    /*
     * 记录发布版本或文档版本的递增编号。
     */
    version_no INTEGER NOT NULL,

    /*
     * 记录此资源的展示名称。
     */
    name TEXT NOT NULL,

    /*
     * 记录此资源的可选说明。
     */
    description TEXT NULL,

    /*
     * 保存发布时冻结的助手配置与执行依赖。
     */
    snapshot_json TEXT NOT NULL,

    /*
     * 记录冻结发布快照的内容摘要。
     */
    snapshot_hash TEXT NOT NULL,

    /*
     * 标记发布版本或访问授权是否已撤回。
     */
    is_revoked INTEGER NOT NULL DEFAULT 0,

    /*
     * 记录授权或版本撤回的时间。
     */
    revoked_at TEXT NULL,

    /*
     * 记录版本发布的时间。
     */
    published_at TEXT NOT NULL
);

/*
 * 加速按 assistant_id, snapshot_hash 查询。
 */

CREATE INDEX ix_assistant_published_version_hash ON assistant_published_version (
    assistant_id,
    snapshot_hash
);

/*
 * 加速按 assistant_key, is_revoked 查询。
 */

CREATE INDEX ix_assistant_published_version_public ON assistant_published_version (
    assistant_key,
    is_revoked
);

/*
 * 保证 assistant_id, version_no 的组合唯一。
 */

CREATE UNIQUE INDEX uq_assistant_published_version_scope_no ON assistant_published_version (
    assistant_id,
    version_no
);

/*
 * 轮次的执行运行，保存执行状态、完成时间和最终事件序号。
 */

CREATE TABLE assistant_run (
    /*
     * 记录当前数据的唯一标识。
     */
    id TEXT NOT NULL PRIMARY KEY,

    /*
     * 关联此记录所属的用户请求轮次。
     */
    turn_id TEXT NOT NULL,

    /*
     * 关联此消息、轮次或运行所属的对话。
     */
    conversation_id TEXT NOT NULL,

    /*
     * 关联创建该对话或请求的匿名会话。
     */
    session_id TEXT NOT NULL,

    /*
     * 关联此数据所属的助手。
     */
    assistant_id TEXT NOT NULL,

    /*
     * 绑定创建此会话或运行时使用的固定发布版本。
     */
    published_version_id TEXT NOT NULL,

    /*
     * 区分公开执行和管理端预览执行。
     */
    execution_mode INTEGER NOT NULL,

    /*
     * 保存本次执行使用的供应商配置及其修订引用。
     */
    provider_profile_ref TEXT NOT NULL,

    /*
     * 记录当前选择的模型标识。
     */
    model TEXT NOT NULL,

    /*
     * 记录当前生命周期状态。
     */
    status INTEGER NOT NULL,

    /*
     * 记录当前已预留事件序号上限。
     */
    sequence_head INTEGER NOT NULL,

    /*
     * 记录执行或索引任务的开始时间。
     */
    started_at TEXT NULL,

    /*
     * 记录处理完成或进入终态的时间。
     */
    completed_at TEXT NULL,

    /*
     * 保存运行或索引失败的稳定错误码。
     */
    failure_code TEXT NULL,

    /*
     * 记录模型输入占用的令牌数量。
     */
    prompt_tokens INTEGER NULL,

    /*
     * 记录模型生成内容占用的令牌数量。
     */
    completion_tokens INTEGER NULL,

    /*
     * 记录本次模型调用消耗的令牌总数。
     */
    total_tokens INTEGER NULL,

    /*
     * 关联 assistant_turn，父记录删除时级联清理当前记录。
     */
    FOREIGN KEY (turn_id) REFERENCES assistant_turn(id) ON DELETE CASCADE
);

/*
 * 加速按 conversation_id 查询。
 */

CREATE INDEX ix_assistant_run_conversation ON assistant_run (
    conversation_id
);

/*
 * 加速按 assistant_id, published_version_id, execution_mode 查询。
 */

CREATE INDEX ix_assistant_run_scope ON assistant_run (
    assistant_id,
    published_version_id,
    execution_mode
);

/*
 * 保证 turn_id 的组合唯一。
 */

CREATE UNIQUE INDEX uq_assistant_run_turn ON assistant_run (
    turn_id
);

/*
 * 模型发起的工具调用，保存输入、输出、状态和实际耗时。
 */

CREATE TABLE assistant_tool_call (
    /*
     * 记录当前数据的唯一标识。
     */
    id TEXT NOT NULL PRIMARY KEY,

    /*
     * 关联产生此事件、消息或工具调用的执行运行。
     */
    run_id TEXT NOT NULL,

    /*
     * 关联当前使用的工具定义。
     */
    tool_id TEXT NOT NULL,

    /*
     * 映射数据库中的“契约版本”列，并由仓储负责读取和持久化。
     */
    contract_version TEXT NOT NULL,

    /*
     * 保存工具调用输入的摘要。
     */
    input_hash TEXT NOT NULL,

    /*
     * 记录当前生命周期状态。
     */
    status INTEGER NOT NULL,

    /*
     * 保存工具执行结果的 JSON 内容。
     */
    output_json TEXT NULL,

    /*
     * 保存运行或索引失败的稳定错误码。
     */
    failure_code TEXT NULL,

    /*
     * 记录处理完成或进入终态的时间。
     */
    completed_at TEXT NULL,

    /*
     * 保存工具执行实际耗时（毫秒），用于统计成功、失败和拒绝调用的耗时。
     */
    duration_ms REAL NULL,

    /*
     * 关联 assistant_run，父记录删除时级联清理当前记录。
     */
    FOREIGN KEY (run_id) REFERENCES assistant_run(id) ON DELETE CASCADE
);

/*
 * 加速按 run_id 查询。
 */

CREATE INDEX ix_assistant_tool_call_run ON assistant_tool_call (
    run_id
);

/*
 * 加速按 tool_id, contract_version 查询。
 */

CREATE INDEX ix_assistant_tool_call_tool ON assistant_tool_call (
    tool_id,
    contract_version
);

/*
 * 对话中的一次请求轮次，通过客户端消息标识保证提交幂等。
 */

CREATE TABLE assistant_turn (
    /*
     * 记录当前数据的唯一标识。
     */
    id TEXT NOT NULL PRIMARY KEY,

    /*
     * 关联此消息、轮次或运行所属的对话。
     */
    conversation_id TEXT NOT NULL,

    /*
     * 关联创建该对话或请求的匿名会话。
     */
    session_id TEXT NOT NULL,

    /*
     * 关联此数据所属的助手。
     */
    assistant_id TEXT NOT NULL,

    /*
     * 绑定创建此会话或运行时使用的固定发布版本。
     */
    published_version_id TEXT NOT NULL,

    /*
     * 保留客户端幂等标识，用于识别同一消息的重试。
     */
    client_message_id TEXT NOT NULL,

    /*
     * 保存请求内容摘要，用于验证重放是否与原请求一致。
     */
    request_hash TEXT NOT NULL,

    /*
     * 记录用户请求在所属对话中的轮次编号。
     */
    turn_no INTEGER NOT NULL,

    /*
     * 区分公开执行和管理端预览执行。
     */
    execution_mode INTEGER NOT NULL,

    /*
     * 记录当前生命周期状态。
     */
    status INTEGER NOT NULL,

    /*
     * 记录处理完成或进入终态的时间。
     */
    completed_at TEXT NULL,

    /*
     * 保存运行或索引失败的稳定错误码。
     */
    failure_code TEXT NULL,

    /*
     * 关联 assistant_conversation，父记录删除时级联清理当前记录。
     */
    FOREIGN KEY (conversation_id) REFERENCES assistant_conversation(id) ON DELETE CASCADE
);

/*
 * 加速按 conversation_id, turn_no 查询。
 */

CREATE INDEX ix_assistant_turn_conversation_order ON assistant_turn (
    conversation_id,
    turn_no
);

/*
 * 加速按 assistant_id, published_version_id, execution_mode 查询。
 */

CREATE INDEX ix_assistant_turn_scope ON assistant_turn (
    assistant_id,
    published_version_id,
    execution_mode
);

/*
 * 保证 conversation_id, client_message_id 的组合唯一。
 */

CREATE UNIQUE INDEX uq_assistant_turn_conversation_client_message ON assistant_turn (
    conversation_id,
    client_message_id
);

/*
 * 本地向量集合，记录集合名称及向量维度。
 */

CREATE TABLE assistant_vector_collection (
    /*
     * 记录此资源的展示名称。
     */
    name TEXT NOT NULL PRIMARY KEY,

    /*
     * 记录嵌入向量的固定维度。
     */
    dimension INTEGER NOT NULL CHECK (dimension > 0)
);

/*
 * 本地向量点，保存片段标识、内容摘要及向量数据。
 */

CREATE TABLE assistant_vector_point (
    /*
     * 记录向量索引所属的集合名称。
     */
    collection_name TEXT NOT NULL,

    /*
     * 关联知识文档的文本片段。
     */
    chunk_id TEXT NOT NULL,

    /*
     * 记录正文的内容摘要，用于检测内容变化。
     */
    content_hash TEXT NOT NULL,

    /*
     * 关联产生此数据的原始文档。
     */
    document_id TEXT NOT NULL,

    /*
     * 记录发布版本或文档版本的递增编号。
     */
    version_no INTEGER NOT NULL,

    /*
     * 记录片段在所属文档中的顺序。
     */
    chunk_index INTEGER NOT NULL,

    /*
     * 保存序列化后的嵌入向量。
     */
    vector_json TEXT NOT NULL,

    /*
     * 按组合字段唯一定位当前记录。
     */
    PRIMARY KEY (collection_name, chunk_id),

    /*
     * 关联 assistant_vector_collection，父记录删除时级联清理当前记录。
     */
    FOREIGN KEY (collection_name) REFERENCES assistant_vector_collection(name)
);
