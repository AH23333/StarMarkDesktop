-- StarMark SQLite Schema v1
-- 对应技术文档 §4 数据模型设计
-- 所有时间戳均为 Unix 秒。

PRAGMA journal_mode = WAL;       -- 读写不互锁
PRAGMA foreign_keys = ON;
PRAGMA synchronous = NORMAL;

-- ============================================================
-- 统一条目主表（§4.1）
-- ============================================================
CREATE TABLE IF NOT EXISTS items (
    id              INTEGER PRIMARY KEY,
    type            TEXT    NOT NULL,                 -- 'file' | 'bookmark' | 'github_star' | 'clipboard'
    source          TEXT    NOT NULL,                 -- 'filesystem' | 'chrome' | 'github' | 'ditto' ...
    source_id       TEXT,                             -- 源内唯一标识
    title           TEXT    NOT NULL DEFAULT '',
    subtitle        TEXT    NOT NULL DEFAULT '',
    uri             TEXT    NOT NULL DEFAULT '',
    search_text     TEXT    NOT NULL DEFAULT '',       -- 拼接后的可搜索文本（FTS5 索引目标）
    description     TEXT,
    stars_count     INTEGER,                           -- GitHub Star 数（仅 github_star 类型有值）
    file_size       INTEGER,                           -- 文件大小字节（仅 file 类型有值）
    created_at      INTEGER NOT NULL,
    updated_at      INTEGER NOT NULL,
    synced_at       INTEGER,
    extra_json      TEXT,                              -- 扩展字段 JSON
    hidden          INTEGER NOT NULL DEFAULT 0,        -- 0/1 用户状态，同步不覆盖
    pinned          INTEGER NOT NULL DEFAULT 0,        -- 0/1 用户置顶，同步不覆盖（v2 迁移列）
    notes           TEXT                               -- 用户状态，同步不覆盖
);

CREATE INDEX IF NOT EXISTS idx_items_type_source ON items(type, source);
CREATE INDEX IF NOT EXISTS idx_items_stars       ON items(stars_count) WHERE stars_count IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_items_updated     ON items(updated_at);
CREATE INDEX IF NOT EXISTS idx_items_hidden      ON items(hidden);
CREATE UNIQUE INDEX IF NOT EXISTS idx_items_source ON items(source, source_id);

-- ============================================================
-- FTS5 外部内容表（§4.2 关键架构）
-- content='items' 指示 FTS5 不自己存文本，通过 rowid 回查 items 表
-- tokenize='porter unicode61'：英文词干还原。
-- 注意：unicode61 并不按字切分中文，它把一段连续 CJK 视为【一个】token，
-- 因此中文检索必须由写入侧展开补偿——见 StarMark.Abstractions.Text.CjkTokenizer。
-- 不要依赖本行分词器处理中文，也不要换成 trigram（实测 2 字词全灭）。
-- ============================================================
CREATE VIRTUAL TABLE IF NOT EXISTS items_fts USING fts5(
    title,
    search_text,
    content='items',
    content_rowid='id',
    tokenize='porter unicode61'
);

-- 同步触发器：items 表变更时自动维护 FTS5 索引
CREATE TRIGGER IF NOT EXISTS items_ai_fts AFTER INSERT ON items BEGIN
    INSERT INTO items_fts(rowid, title, search_text)
    VALUES (new.id, new.title, new.search_text);
END;

CREATE TRIGGER IF NOT EXISTS items_ad_fts AFTER DELETE ON items BEGIN
    INSERT INTO items_fts(items_fts, rowid, title, search_text)
    VALUES('delete', old.id, old.title, old.search_text);
END;

CREATE TRIGGER IF NOT EXISTS items_au_fts AFTER UPDATE ON items BEGIN
    INSERT INTO items_fts(items_fts, rowid, title, search_text)
    VALUES('delete', old.id, old.title, old.search_text);
    INSERT INTO items_fts(rowid, title, search_text)
    VALUES (new.id, new.title, new.search_text);
END;

-- ============================================================
-- 标签与关联（§4.3）
-- ============================================================
CREATE TABLE IF NOT EXISTS tags (
    id          INTEGER PRIMARY KEY,
    name        TEXT NOT NULL UNIQUE COLLATE NOCASE,
    color       TEXT,
    created_at  INTEGER NOT NULL,
    extra_json  TEXT
);

CREATE TABLE IF NOT EXISTS item_tags (
    item_id     INTEGER NOT NULL,
    tag_id      INTEGER NOT NULL,
    created_at  INTEGER NOT NULL,
    PRIMARY KEY (item_id, tag_id),
    FOREIGN KEY (item_id) REFERENCES items(id) ON DELETE CASCADE,
    FOREIGN KEY (tag_id)  REFERENCES tags(id)  ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_item_tags_tag ON item_tags(tag_id);

-- ============================================================
-- 笔记（§4.4）
-- ============================================================
CREATE TABLE IF NOT EXISTS notes (
    id          INTEGER PRIMARY KEY,
    item_id     INTEGER NOT NULL,
    content     TEXT NOT NULL,
    created_at  INTEGER NOT NULL,
    updated_at  INTEGER NOT NULL,
    FOREIGN KEY (item_id) REFERENCES items(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_notes_item ON notes(item_id);

-- ============================================================
-- 规则引擎（§4.5）
-- ============================================================
CREATE TABLE IF NOT EXISTS rules (
    id              INTEGER PRIMARY KEY,
    name            TEXT NOT NULL,
    enabled         INTEGER NOT NULL DEFAULT 1,
    conditions_json TEXT NOT NULL,
    actions_json    TEXT NOT NULL,
    priority        INTEGER NOT NULL DEFAULT 0,
    created_at      INTEGER NOT NULL,
    updated_at      INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS rule_runs (
    id              INTEGER PRIMARY KEY,
    rule_id         INTEGER NOT NULL,
    item_id         INTEGER NOT NULL,
    triggered_at    INTEGER NOT NULL,
    result          TEXT,
    error_message   TEXT,
    FOREIGN KEY (rule_id) REFERENCES rules(id)
);

-- ============================================================
-- 同步状态（§4.6 配套）
-- ============================================================
CREATE TABLE IF NOT EXISTS sync_state (
    key             TEXT PRIMARY KEY,
    value           TEXT NOT NULL
);
