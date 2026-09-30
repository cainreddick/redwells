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

test('copy forward carries income, bills, pot and debt balances', () => {
  const db = fresh();
  const sep = repo.createMonth(db, { year: 2026, month: 9 });
  repo.addItem(db, 'income', sep, { name: 'Salary', amount: 250000, recurring: true });
  repo.addItem(db, 'income', sep, { name: 'Bonus', amount: 50000, recurring: false });
  repo.addItem(db, 'bills', sep, { name: 'Rent', amount: 100000, category: 'Housing', dueDay: 1 });
  repo.addItem(db, 'pots', sep, { name: 'Holiday', target: 200000, opening: 50000, contribution: 10000, withdrawal: 5000 });
  repo.addItem(db, 'debts', sep, { name: 'Card', opening: 100000, payment: 10000, apr: 12 });
  repo.addItem(db, 'debts', sep, { name: 'Loan', opening: 3000, payment: 3000, apr: null });
  repo.addItem(db, 'debts', sep, { name: 'Friend', opening: 8000, payment: 5000, apr: null });
  const source = repo.getMonth(db, sep);

  const { monthId, report } = repo.createMonthFromCopy(db, sep, { year: 2026, month: 10 });
  const oct = repo.getMonth(db, monthId);

  assert.equal(oct.month.label, 'October 2026');
  assert.deepEqual(oct.income.map((i) => [i.name, i.amount]), [['Salary', 250000]]);
  assert.deepEqual(oct.bills.map(({ id, ...b }) => b), [{ name: 'Rent', amount: 100000, category: 'Housing', dueDay: 1 }]);

  const pot = oct.pots[0];
  assert.equal(pot.opening, 55000); // 50000 + 10000 − 5000
  assert.equal(pot.contribution, 10000);
  assert.equal(pot.withdrawal, 0);
  assert.equal(pot.target, 200000);
  assert.equal(pot.seriesId, source.pots[0].seriesId);

  const card = oct.debts.find((d) => d.name === 'Card');
  assert.equal(card.opening, 91000); // 100000 + 1000 interest − 10000
  assert.equal(card.payment, 10000);
  assert.equal(card.apr, 12);
  assert.equal(card.seriesId, source.debts[0].seriesId);

  // Loan was paid off; Friend owes £30 but pays £50 → trimmed to £30.
  assert.equal(oct.debts.some((d) => d.name === 'Loan'), false);
  const friend = oct.debts.find((d) => d.name === 'Friend');
  assert.equal(friend.opening, 3000);
  assert.equal(friend.payment, 3000);

  assert.deepEqual(report, {
    source: 'September 2026',
    skippedIncome: ['Bonus'],
    paidOffDebts: ['Loan'],
    adjustedPayments: [{ name: 'Friend', from: 5000, to: 3000 }],
  });

  // Source month is untouched.
  assert.deepEqual(repo.getMonth(db, sep).income.length, 2);
});

test('copy forward refuses an existing target or a later source', () => {
  const db = fresh();
  const oct = repo.createMonth(db, { year: 2026, month: 10 });
  repo.addItem(db, 'income', oct, { name: 'Salary', amount: 1, recurring: true });
  repo.createMonth(db, { year: 2026, month: 11 });
  assert.throws(() => repo.createMonthFromCopy(db, oct, { year: 2026, month: 11 }), { status: 409 });
  assert.throws(() => repo.createMonthFromCopy(db, oct, { year: 2026, month: 9 }), { status: 400 });
  assert.throws(() => repo.createMonthFromCopy(db, oct, { year: 2026, month: 10 }), { status: 400 });
  assert.throws(() => repo.createMonthFromCopy(db, 999, { year: 2027, month: 1 }), { status: 404 });
  // Failed copies leave nothing behind.
  assert.equal(repo.listMonths(db).length, 2);
});

test('copy forward across a year boundary and over a gap', () => {
  const db = fresh();
  const nov = repo.createMonth(db, { year: 2026, month: 11 });
  repo.addItem(db, 'pots', nov, { name: 'ISA', target: null, opening: 0, contribution: 20000, withdrawal: 0 });
  const { monthId } = repo.createMonthFromCopy(db, nov, { year: 2027, month: 2 });
  assert.equal(repo.getMonth(db, monthId).month.label, 'February 2027');
  assert.equal(repo.getMonth(db, monthId).pots[0].opening, 20000);
});
