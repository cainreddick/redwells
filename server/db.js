// SQLite storage: opening the file, schema migrations, transactions and snapshots.

import { DatabaseSync } from 'node:sqlite';
import fs from 'node:fs';
import path from 'node:path';

// Each entry upgrades the schema by one version (tracked in PRAGMA user_version).
// Never edit a released migration — append a new one.
const MIGRATIONS = [
  `
  CREATE TABLE months (
    id         INTEGER PRIMARY KEY,
    year       INTEGER NOT NULL CHECK (year BETWEEN 2000 AND 2100),
    month      INTEGER NOT NULL CHECK (month BETWEEN 1 AND 12),
    notes      TEXT    NOT NULL DEFAULT '',
    created_at TEXT    NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%SZ', 'now')),
    updated_at TEXT    NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%SZ', 'now')),
    UNIQUE (year, month)
  );

  CREATE TABLE income (
    id        INTEGER PRIMARY KEY,
    month_id  INTEGER NOT NULL REFERENCES months(id) ON DELETE CASCADE,
    position  INTEGER NOT NULL,
    name      TEXT    NOT NULL,
    amount    INTEGER NOT NULL CHECK (amount >= 0),
    recurring INTEGER NOT NULL DEFAULT 1 CHECK (recurring IN (0, 1))
  );

  CREATE TABLE bills (
    id       INTEGER PRIMARY KEY,
    month_id INTEGER NOT NULL REFERENCES months(id) ON DELETE CASCADE,
    position INTEGER NOT NULL,
    name     TEXT    NOT NULL,
    amount   INTEGER NOT NULL CHECK (amount >= 0),
    category TEXT,
    due_day  INTEGER CHECK (due_day BETWEEN 1 AND 31)
  );

  -- series_id links the "same" pot or debt across months, so history survives renames.
  CREATE TABLE pots (
    id           INTEGER PRIMARY KEY,
    month_id     INTEGER NOT NULL REFERENCES months(id) ON DELETE CASCADE,
    series_id    TEXT    NOT NULL,
    position     INTEGER NOT NULL,
    name         TEXT    NOT NULL,
    target       INTEGER CHECK (target >= 0),
    opening      INTEGER NOT NULL CHECK (opening >= 0),
    contribution INTEGER NOT NULL DEFAULT 0 CHECK (contribution >= 0),
    withdrawal   INTEGER NOT NULL DEFAULT 0 CHECK (withdrawal >= 0),
    UNIQUE (month_id, series_id)
  );

  CREATE TABLE debts (
    id        INTEGER PRIMARY KEY,
    month_id  INTEGER NOT NULL REFERENCES months(id) ON DELETE CASCADE,
    series_id TEXT    NOT NULL,
    position  INTEGER NOT NULL,
    name      TEXT    NOT NULL,
    opening   INTEGER NOT NULL CHECK (opening >= 0),
    payment   INTEGER NOT NULL DEFAULT 0 CHECK (payment >= 0),
    apr       REAL    CHECK (apr BETWEEN 0 AND 100),
    UNIQUE (month_id, series_id)
  );

  CREATE INDEX income_month ON income (month_id);
  CREATE INDEX bills_month  ON bills  (month_id);
  CREATE INDEX pots_month   ON pots   (month_id);
  CREATE INDEX debts_month  ON debts  (month_id);
  `,
];

export const SCHEMA_VERSION = MIGRATIONS.length;

export function openDatabase(file) {
  if (file !== ':memory:') fs.mkdirSync(path.dirname(path.resolve(file)), { recursive: true });
  const db = new DatabaseSync(file);
  // Rollback journal (not WAL) keeps everything in the one .db file, so copying it is a backup.
  db.exec(`
    PRAGMA journal_mode = DELETE;
    PRAGMA synchronous = FULL;
    PRAGMA foreign_keys = ON;
  `);
  migrate(db);
  return db;
}

function migrate(db) {
  const current = db.prepare('PRAGMA user_version').get().user_version;
  if (current > MIGRATIONS.length) {
    throw new Error(
      `Database schema v${current} is newer than this app understands (v${MIGRATIONS.length}). Update the app.`,
    );
  }
  for (let v = current; v < MIGRATIONS.length; v++) {
    transaction(db, () => {
      db.exec(MIGRATIONS[v]);
      db.exec(`PRAGMA user_version = ${v + 1}`);
    });
  }
}

// Tracked by hand because DatabaseSync#isTransaction only arrived in Node 22.16.
const inTransaction = new WeakSet();

/** Run fn inside a transaction; nested calls join the outer one. */
export function transaction(db, fn) {
  if (inTransaction.has(db)) return fn();
  db.exec('BEGIN IMMEDIATE');
  inTransaction.add(db);
  try {
    const result = fn();
    db.exec('COMMIT');
    return result;
  } catch (err) {
    db.exec('ROLLBACK');
    throw err;
  } finally {
    inTransaction.delete(db);
  }
}

/**
 * Write a consistent copy of the database to dir/<prefix>-<timestamp>.db and prune
 * old copies with the same prefix beyond `keep`. Returns the new file's path.
 */
export function snapshot(db, dir, { prefix = 'budget', keep = 10 } = {}) {
  fs.mkdirSync(dir, { recursive: true });
  const stamp = new Date().toISOString().replace(/[:.]/g, '-').replace('T', '_').slice(0, 19);
  let target = path.join(dir, `${prefix}-${stamp}.db`);
  for (let i = 2; fs.existsSync(target); i++) target = path.join(dir, `${prefix}-${stamp}-${i}.db`);
  db.exec(`VACUUM INTO '${target.replaceAll("'", "''")}'`);

  const pattern = new RegExp(`^${prefix}-.*\\.db$`);
  const old = fs.readdirSync(dir).filter((f) => pattern.test(f)).sort().reverse().slice(keep);
  for (const f of old) fs.rmSync(path.join(dir, f));
  return target;
}
