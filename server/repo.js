// Data access: the only module that knows table and column names.
// Items cross this boundary in API shape (camelCase, pence integers, booleans).

import { randomUUID } from 'node:crypto';
import { transaction } from './db.js';
import { enrichMonth, monthLabel } from '../shared/calc.js';

export class AppError extends Error {
  constructor(status, message, details) {
    super(message);
    this.status = status;
    this.details = details;
  }
}

// API field → column, per section. `series` sections carry a series_id across months.
const SECTIONS = {
  income: { table: 'income', columns: { name: 'name', amount: 'amount', recurring: 'recurring' } },
  bills: { table: 'bills', columns: { name: 'name', amount: 'amount', category: 'category', dueDay: 'due_day' } },
  pots: {
    table: 'pots',
    series: true,
    columns: { name: 'name', target: 'target', opening: 'opening', contribution: 'contribution', withdrawal: 'withdrawal' },
  },
  debts: {
    table: 'debts',
    series: true,
    columns: { name: 'name', opening: 'opening', payment: 'payment', apr: 'apr' },
  },
};

function section(name) {
  const s = SECTIONS[name];
  if (!s) throw new AppError(404, `Unknown section: ${name}`);
  return s;
}

function rowToItem(sec, row) {
  const item = { id: row.id };
  if (sec.series) item.seriesId = row.series_id;
  for (const [key, col] of Object.entries(sec.columns)) item[key] = row[col];
  if ('recurring' in item) item.recurring = item.recurring === 1;
  return item;
}

function toColumnValue(v) {
  return typeof v === 'boolean' ? (v ? 1 : 0) : v;
}

function monthRow(row) {
  return {
    id: row.id,
    year: row.year,
    month: row.month,
    label: monthLabel(row.year, row.month),
    notes: row.notes,
    createdAt: row.created_at,
    updatedAt: row.updated_at,
  };
}

function itemsFor(db, sectionName, monthIds) {
  const sec = SECTIONS[sectionName];
  const byMonth = new Map(monthIds.map((id) => [id, []]));
  if (monthIds.length === 0) return byMonth;
  const rows = db
    .prepare(
      `SELECT * FROM ${sec.table} WHERE month_id IN (${monthIds.map(() => '?').join(', ')})
       ORDER BY month_id, position, id`,
    )
    .all(...monthIds);
  for (const row of rows) byMonth.get(row.month_id)?.push(rowToItem(sec, row));
  return byMonth;
}

function assemble(db, monthRows) {
  const ids = monthRows.map((r) => r.id);
  const items = Object.fromEntries(Object.keys(SECTIONS).map((s) => [s, itemsFor(db, s, ids)]));
  return monthRows.map((row) =>
    enrichMonth({
      month: monthRow(row),
      income: items.income.get(row.id),
      bills: items.bills.get(row.id),
      pots: items.pots.get(row.id),
      debts: items.debts.get(row.id),
    }),
  );
}

// ---------------------------------------------------------------- months

/** All months, newest first, each with a summary (items omitted to keep it light). */
export function listMonths(db) {
  const rows = db.prepare('SELECT * FROM months ORDER BY year DESC, month DESC').all();
  return assemble(db, rows).map(({ month, summary }) => ({ ...month, summary }));
}

/** Full month payloads (with items) in chronological order — used for charts and backups. */
export function allMonthsDetailed(db) {
  return assemble(db, db.prepare('SELECT * FROM months ORDER BY year, month').all());
}

export function getMonth(db, id) {
  const row = db.prepare('SELECT * FROM months WHERE id = ?').get(id);
  return row ? assemble(db, [row])[0] : null;
}

export function requireMonth(db, id) {
  const m = getMonth(db, id);
  if (!m) throw new AppError(404, 'Month not found');
  return m;
}

export function findMonthId(db, year, month) {
  return db.prepare('SELECT id FROM months WHERE year = ? AND month = ?').get(year, month)?.id ?? null;
}

export function createMonth(db, { year, month, notes = '' }) {
  if (findMonthId(db, year, month) !== null) {
    throw new AppError(409, `${monthLabel(year, month)} already exists`);
  }
  const r = db.prepare('INSERT INTO months (year, month, notes) VALUES (?, ?, ?)').run(year, month, notes);
  return Number(r.lastInsertRowid);
}

export function updateMonthNotes(db, id, notes) {
  const r = db
    .prepare(`UPDATE months SET notes = ?, updated_at = strftime('%Y-%m-%dT%H:%M:%SZ', 'now') WHERE id = ?`)
    .run(notes, id);
  if (r.changes === 0) throw new AppError(404, 'Month not found');
}

export function deleteMonth(db, id) {
  const r = db.prepare('DELETE FROM months WHERE id = ?').run(id);
  if (r.changes === 0) throw new AppError(404, 'Month not found');
}

function touchMonth(db, id) {
  db.prepare(`UPDATE months SET updated_at = strftime('%Y-%m-%dT%H:%M:%SZ', 'now') WHERE id = ?`).run(id);
}

// ---------------------------------------------------------------- items

/** Insert a validated item. Pass seriesId to continue an existing pot/debt series. */
export function addItem(db, sectionName, monthId, value, { seriesId } = {}) {
  const sec = section(sectionName);
  return transaction(db, () => {
    if (!db.prepare('SELECT 1 FROM months WHERE id = ?').get(monthId)) throw new AppError(404, 'Month not found');
    const cols = ['month_id', 'position'];
    const { next } = db
      .prepare(`SELECT COALESCE(MAX(position), 0) + 1 AS next FROM ${sec.table} WHERE month_id = ?`)
      .get(monthId);
    const vals = [monthId, next];
    if (sec.series) {
      cols.push('series_id');
      vals.push(seriesId ?? randomUUID());
    }
    for (const [key, col] of Object.entries(sec.columns)) {
      cols.push(col);
      vals.push(toColumnValue(value[key]));
    }
    const sql = `INSERT INTO ${sec.table} (${cols.join(', ')}) VALUES (${cols.map(() => '?').join(', ')})`;
    const id = Number(db.prepare(sql).run(...vals).lastInsertRowid);
    touchMonth(db, monthId);
    return id;
  });
}

export function getItem(db, sectionName, itemId) {
  const sec = section(sectionName);
  const row = db.prepare(`SELECT * FROM ${sec.table} WHERE id = ?`).get(itemId);
  return row ? { monthId: row.month_id, item: rowToItem(sec, row) } : null;
}

/** Replace an item's editable fields. Returns the owning month id. */
export function updateItem(db, sectionName, itemId, value) {
  const sec = section(sectionName);
  return transaction(db, () => {
    const found = getItem(db, sectionName, itemId);
    if (!found) throw new AppError(404, 'Item not found');
    const entries = Object.entries(sec.columns);
    const sql = `UPDATE ${sec.table} SET ${entries.map(([, col]) => `${col} = ?`).join(', ')} WHERE id = ?`;
    db.prepare(sql).run(...entries.map(([key]) => toColumnValue(value[key])), itemId);
    touchMonth(db, found.monthId);
    return found.monthId;
  });
}

/** Delete an item. Returns the owning month id. */
export function deleteItem(db, sectionName, itemId) {
  const sec = section(sectionName);
  return transaction(db, () => {
    const found = getItem(db, sectionName, itemId);
    if (!found) throw new AppError(404, 'Item not found');
    db.prepare(`DELETE FROM ${sec.table} WHERE id = ?`).run(itemId);
    touchMonth(db, found.monthId);
    return found.monthId;
  });
}
