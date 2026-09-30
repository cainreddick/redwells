import { test } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { openDatabase, snapshot, SCHEMA_VERSION } from '../server/db.js';
import * as repo from '../server/repo.js';

const fresh = () => openDatabase(':memory:');

test('schema is created and versioned', () => {
  const db = fresh();
  assert.equal(db.prepare('PRAGMA user_version').get().user_version, SCHEMA_VERSION);
  assert.equal(db.prepare('PRAGMA foreign_keys').get().foreign_keys, 1);
});

test('months: create, list newest first, reject duplicates', () => {
  const db = fresh();
  repo.createMonth(db, { year: 2026, month: 9 });
  repo.createMonth(db, { year: 2026, month: 10 });
  repo.createMonth(db, { year: 2025, month: 12 });
  assert.deepEqual(repo.listMonths(db).map((m) => m.label), ['October 2026', 'September 2026', 'December 2025']);
  assert.throws(() => repo.createMonth(db, { year: 2026, month: 10 }), { status: 409 });
});

test('items: add, update, delete, with computed fields', () => {
  const db = fresh();
  const monthId = repo.createMonth(db, { year: 2026, month: 10 });
  repo.addItem(db, 'income', monthId, { name: 'Salary', amount: 250000, recurring: true });
  repo.addItem(db, 'bills', monthId, { name: 'Rent', amount: 100000, category: 'Housing', dueDay: 1 });
  repo.addItem(db, 'pots', monthId, { name: 'Holiday', target: 200000, opening: 50000, contribution: 10000, withdrawal: 0 });
  repo.addItem(db, 'debts', monthId, { name: 'Card', opening: 100000, payment: 10000, apr: 12 });

  let m = repo.getMonth(db, monthId);
  assert.equal(m.income[0].recurring, true);
  assert.equal(m.bills[0].dueDay, 1);
  assert.equal(m.pots[0].closing, 60000);
  assert.equal(m.pots[0].progress, 0.3);
  assert.match(m.pots[0].seriesId, /^[0-9a-f-]{36}$/);
  assert.equal(m.debts[0].closing, 91000);
  assert.equal(m.summary.leftOver, 250000 - 100000 - 10000 - 10000);

  repo.updateItem(db, 'income', m.income[0].id, { name: 'Salary (net)', amount: 260000, recurring: false });
  m = repo.getMonth(db, monthId);
  assert.deepEqual(m.income[0], { id: m.income[0].id, name: 'Salary (net)', amount: 260000, recurring: false });

  repo.deleteItem(db, 'bills', m.bills[0].id);
  assert.equal(repo.getMonth(db, monthId).bills.length, 0);
  assert.throws(() => repo.deleteItem(db, 'bills', 999), { status: 404 });
});

test('items keep insertion order', () => {
  const db = fresh();
  const monthId = repo.createMonth(db, { year: 2026, month: 10 });
  for (const name of ['C', 'A', 'B']) repo.addItem(db, 'income', monthId, { name, amount: 1, recurring: true });
  assert.deepEqual(repo.getMonth(db, monthId).income.map((i) => i.name), ['C', 'A', 'B']);
});

test('deleting a month cascades to its items', () => {
  const db = fresh();
  const monthId = repo.createMonth(db, { year: 2026, month: 10 });
  repo.addItem(db, 'income', monthId, { name: 'Salary', amount: 1, recurring: true });
  repo.addItem(db, 'debts', monthId, { name: 'Card', opening: 1, payment: 0, apr: null });
  repo.deleteMonth(db, monthId);
  assert.equal(db.prepare('SELECT COUNT(*) AS n FROM income').get().n, 0);
  assert.equal(db.prepare('SELECT COUNT(*) AS n FROM debts').get().n, 0);
  assert.equal(repo.getMonth(db, monthId), null);
});

test('database constraints back up validation', () => {
  const db = fresh();
  const monthId = repo.createMonth(db, { year: 2026, month: 10 });
  assert.throws(() => repo.addItem(db, 'income', monthId, { name: 'x', amount: -1, recurring: true }), /CHECK/);
  assert.throws(() => repo.addItem(db, 'income', 999, { name: 'x', amount: 1, recurring: true }), { status: 404 });
});

test('data persists across reopen, and snapshots prune old copies', () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'budget-test-'));
  try {
    const file = path.join(dir, 'budget.db');
    let db = openDatabase(file);
    const monthId = repo.createMonth(db, { year: 2026, month: 10 });
    repo.addItem(db, 'income', monthId, { name: 'Salary', amount: 250000, recurring: true });
    db.close();

    // Only the single .db file should exist — no -wal / -shm / -journal left behind.
    assert.deepEqual(fs.readdirSync(dir), ['budget.db']);

    db = openDatabase(file);
    assert.equal(repo.getMonth(db, monthId).income[0].amount, 250000);

    const backups = path.join(dir, 'backups');
    for (let i = 0; i < 4; i++) snapshot(db, backups, { keep: 3 });
    const files = fs.readdirSync(backups);
    assert.equal(files.length, 3);

    const copy = openDatabase(path.join(backups, files[0]));
    assert.equal(repo.listMonths(copy)[0].summary.totalIncome, 250000);
    copy.close();
    db.close();
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});
