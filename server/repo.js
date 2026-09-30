// Data access: the only module that knows table and column names.
// Items cross this boundary in API shape (camelCase, pence integers, booleans).

import { randomUUID } from 'node:crypto';
import { transaction } from './db.js';
import { enrichMonth, monthLabel, monthKey, monthlyInterest } from '../shared/calc.js';

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

/**
 * Chart data for saved months between two "YYYY-MM" keys (inclusive; either may be omitted).
 * Pot and debt series are ordered by first appearance across *all* months, and `index` is
 * that position, so a series keeps its colour whatever range is picked. Each series has one
 * closing balance per month in range (null where it didn't exist) and its most recent name.
 */
export function history(db, { from, to } = {}) {
  const all = allMonthsDetailed(db);
  const keyOf = (m) => monthKey(m.month.year, m.month.month);
  const inRange = all.filter((m) => (!from || keyOf(m) >= from) && (!to || keyOf(m) <= to));

  const seriesFor = (section) => {
    const byId = new Map();
    for (const m of all) {
      for (const it of m[section]) {
        const s = byId.get(it.seriesId) ?? { seriesId: it.seriesId, index: byId.size };
        s.name = it.name;
        byId.set(it.seriesId, s);
      }
    }
    return [...byId.values()]
      .map((s) => ({
        ...s,
        balances: inRange.map((m) => m[section].find((it) => it.seriesId === s.seriesId)?.closing ?? null),
      }))
      .filter((s) => s.balances.some((v) => v !== null));
  };

  return {
    available: all.map((m) => ({ key: keyOf(m), label: m.month.label })),
    months: inRange.map((m) => ({
      key: keyOf(m), label: m.month.label, year: m.month.year, month: m.month.month, summary: m.summary,
    })),
    pots: seriesFor('pots'),
    debts: seriesFor('debts'),
  };
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

/**
 * Create a month by carrying another one forward:
 * - recurring income and all bills are copied as-is (one-off income is left behind)
 * - each pot's closing balance becomes the new opening balance; the contribution carries,
 *   withdrawals reset to zero
 * - each debt's closing balance (after interest and payment) becomes the new opening
 *   balance; debts that reached zero are not carried, and a payment larger than what will
 *   be owed is trimmed to the final amount
 * Returns the new month id and a report of anything that wasn't carried unchanged.
 */
export function createMonthFromCopy(db, sourceId, target) {
  return transaction(db, () => {
    const src = requireMonth(db, sourceId);
    if (monthKey(src.month.year, src.month.month) >= monthKey(target.year, target.month)) {
      throw new AppError(400, `Can only copy forward from an earlier month (${src.month.label} is not before ${monthLabel(target.year, target.month)})`);
    }
    const monthId = createMonth(db, target);
    const report = { source: src.month.label, skippedIncome: [], paidOffDebts: [], adjustedPayments: [] };

    for (const { id, ...item } of src.income) {
      if (item.recurring) addItem(db, 'income', monthId, item);
      else report.skippedIncome.push(item.name);
    }
    for (const { id, ...item } of src.bills) addItem(db, 'bills', monthId, item);

    for (const p of src.pots) {
      addItem(
        db, 'pots', monthId,
        { name: p.name, target: p.target, opening: p.closing, contribution: p.contribution, withdrawal: 0 },
        { seriesId: p.seriesId },
      );
    }

    for (const d of src.debts) {
      if (d.closing === 0) {
        report.paidOffDebts.push(d.name);
        continue;
      }
      const owed = d.closing + monthlyInterest(d.closing, d.apr);
      const payment = Math.min(d.payment, owed);
      if (payment !== d.payment) report.adjustedPayments.push({ name: d.name, from: d.payment, to: payment });
      addItem(db, 'debts', monthId, { name: d.name, opening: d.closing, payment, apr: d.apr }, { seriesId: d.seriesId });
    }

    return { monthId, report };
  });
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
