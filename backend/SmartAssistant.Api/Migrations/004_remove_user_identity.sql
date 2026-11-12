/*
 * 独立应用不提供登录和用户体系，移除预览及发布记录中的操作人字段。
 * 保留助手关联、发布与撤回时间、修订号和匿名会话状态。
 * 本迁移在事务中执行，不重建关联表，保留记录及原有写入顺序。
 */

/*
 * 预览按助手及快照标识访问，不再依赖创建者身份。
 */
DROP INDEX ix_assistant_preview_scope;

ALTER TABLE assistant_preview_snapshot DROP COLUMN actor_id;

CREATE INDEX ix_assistant_preview_scope ON assistant_preview_snapshot (
    assistant_id
);

/*
 * 发布版本仅保存发布及撤回状态和业务时间。
 */
ALTER TABLE assistant_published_version DROP COLUMN published_by;

ALTER TABLE assistant_published_version DROP COLUMN revoked_by;

/*
 * 匿名会话继续保留有效期及撤回状态，不记录执行撤回的用户。
 */
ALTER TABLE assistant_anonymous_session DROP COLUMN revoked_by;
