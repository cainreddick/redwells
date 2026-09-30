import { test } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { openDatabase } from '../server/db.js';
import { createServer } from '../server/app.js';
import * as repo from '../server/repo.js';
import { buildBackup, monthToCsv, parseBackup, restoreBackup } from '../server/export.js';

function sampleDb() {
  const db = openDatabase(':memory:');
  const sep = repo.createMonth(db, { year: 2026, month: 9, notes: 'MOT due, "don\'t forget"' });
  repo.addItem(db, 'income', sep, { name: 'Salary', amount: 250000, recurring: true });
  repo.addItem(db, 'income', sep, { name: '=HYPERLINK("x")', amount: 100, recurring: false });
  repo.addItem(db, 'bills', sep, { name: 'Rent, flat', amount: 100000, category: 'Housing', dueDay: 31 });
  repo.addItem(db, 'pots', sep, { name: 'Holiday', target: 200000, opening: 50000, contribution: 10000, withdrawal: 0 });
  repo.addItem(db, 'debts', sep, { name: 'Card', opening: 100000, payment: 10000, apr: 12 });
  repo.createMonthFromCopy(db, sep, { year: 2026, month: 10 });
  return { db, sep };
}

test('month CSV: sections, plain-number amounts, UK dates, escaping', () => {
  const { db, sep } = sampleDb();
  const csv = monthToCsv(repo.getMonth(db, sep), new Date('2026-09-30T13:05:00Z'));
  assert.ok(csv.startsWith('﻿Household Budget,September 2026\r\n'));
  const lines = csv.slice(1).split('\r\n');
  assert.ok(lines.includes('Exported,"30/09/2026, 14:05"')); // BST
  assert.ok(lines.includes('Total income,2501.00'));
  assert.ok(lines.includes('Left over,1301.00'));
  assert.ok(lines.includes('Salary,Monthly,2500.00'));
  assert.ok(lines.includes(`"'=HYPERLINK(""x"")",One-off,1.00`)); // formula neutralised, quotes doubled
  assert.ok(lines.includes('"Rent, flat",Housing,30/09/2026,1000.00')); // due 31st → last day of Sept
  assert.ok(lines.includes('Holiday,500.00,100.00,0.00,600.00,2000.00,30'));
  assert.ok(lines.includes('Card,1000.00,12,10.00,100.00,910.00,July 2027 (11 months)'));
  assert.ok(lines.includes(`"MOT due, ""don't forget"""`));

  // Negative amounts stay plain numbers (the formula guard only applies to text).
  const rent = repo.getMonth(db, sep).bills[0];
  repo.updateItem(db, 'bills', rent.id, { ...rent, amount: 300000 });
  assert.ok(monthToCsv(repo.getMonth(db, sep)).includes('\r\nLeft over,-699.00\r\n'));
});

test('backup round-trips into a fresh database exactly', () => {
  const { db } = sampleDb();
  const backup = JSON.parse(JSON.stringify(buildBackup(db)));
  assert.equal(backup.app, 'household-budget');
  assert.equal(backup.months.length, 2);
  assert.equal('closing' in backup.months[0].pots[0], false); // computed fields aren't stored

  const other = openDatabase(':memory:');
  repo.createMonth(other, { year: 2020, month: 1 }); // will be replaced
  restoreBackup(other, parseBackup(backup));
  assert.deepEqual(buildBackup(other).months, backup.months);
  assert.deepEqual(repo.listMonths(other).map((m) => m.label), ['October 2026', 'September 2026']);
  // Series links survive, so charts still join pots across months.
  const h = repo.history(other);
  assert.equal(h.pots.length, 1);
  assert.deepEqual(h.pots[0].balances, [60000, 70000]);
});

test('parseBackup rejects bad files with a specific message', () => {
  const { db } = sampleDb();
  const good = buildBackup(db);
  const clone = () => JSON.parse(JSON.stringify(good));
  assert.throws(() => parseBackup({ hello: 1 }), /isn't a Household Budget backup/);
  assert.throws(() => parseBackup({ ...clone(), format: 99 }), /format 99/);

  const dup = clone();
  dup.months[1].month = 9;
  assert.throws(() => parseBackup(dup), /September 2026 appears twice/);

  const neg = clone();
  neg.months[0].bills[0].amount = -5;
  assert.throws(() => parseBackup(neg), /September 2026 → Fixed bills → "Rent, flat": amount — Cannot be negative/);

  const noSeries = clone();
  delete noSeries.months[0].pots[0].seriesId;
  assert.match(parseBackup(noSeries)[0].pots[0].seriesId, /^[0-9a-f-]{36}$/);
});

test('failed restore leaves existing data untouched', () => {
  const { db } = sampleDb();
  const before = buildBackup(db).months;
  const months = parseBackup(buildBackup(db));
  months[1].income.push({ name: 'Broken', amount: -1, recurring: true }); // bypasses validation → CHECK fails
  assert.throws(() => restoreBackup(db, months), /CHECK/);
  assert.deepEqual(buildBackup(db).months, before);
});

test('HTTP: CSV download, backup download, restore with pre-restore snapshot', async () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'budget-export-'));
  const { db, sep } = sampleDb();
  const server = createServer({ db, dataFile: path.join(dir, 'budget.db'), backupDir: dir, log: { error() {} } });
  await new Promise((r) => server.listen(0, '127.0.0.1', r));
  const base = `http://localhost:${server.address().port}`;
  try {
    const csv = await fetch(`${base}/api/months/${sep}/export.csv`);
    assert.equal(csv.status, 200);
    assert.match(csv.headers.get('content-type'), /text\/csv/);
    assert.equal(csv.headers.get('content-disposition'), 'attachment; filename="budget-2026-09.csv"');

    const b = await fetch(`${base}/api/backup`);
    assert.match(b.headers.get('content-disposition'), /budget-backup-\d{4}-\d{2}-\d{2}\.json/);
    const backup = await b.json();
    backup.months.pop(); // restore only September

    const r = await fetch(`${base}/api/restore`, {
      method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(backup),
    });
    const body = await r.json();
    assert.equal(r.status, 200);
    assert.equal(body.months, 1);
    assert.match(body.snapshot, /^pre-restore-.*\.db$/);
    assert.ok(fs.existsSync(path.join(dir, body.snapshot)));

    const info = await (await fetch(`${base}/api/info`)).json();
    assert.equal(info.snapshots[0].name, body.snapshot);

    const bad = await fetch(`${base}/api/restore`, {
      method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '{"app":"nope"}',
    });
    assert.equal(bad.status, 400);
    assert.equal(repo.listMonths(db).length, 1);
  } finally {
    server.close();
    fs.rmSync(dir, { recursive: true, force: true });
  }
});
