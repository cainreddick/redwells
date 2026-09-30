// CSV export of a month, and JSON backup/restore of everything.

import { randomUUID } from 'node:crypto';
import * as repo from './repo.js';
import { AppError } from './repo.js';
import { transaction, SCHEMA_VERSION } from './db.js';
import { dueDateLabel, monthLabel, monthKey, penceToInput, MONTH_NAMES } from '../shared/calc.js';
import { SECTION_NAMES, validateItem, validateMonth } from '../shared/validate.js';

// ---------------------------------------------------------------- CSV

/**
 * Quote a cell. Text that a spreadsheet would run as a formula (=, +, -, @) is prefixed
 * with an apostrophe; numbers are written as plain numbers.
 */
class Num {
  constructor(text) { this.text = text; }
}

function csvCell(v) {
  if (v == null) return '';
  if (typeof v === 'number') return String(v);
  if (v instanceof Num) return v.text;
  let s = String(v);
  if (/^[=+\-@\t\r]/.test(s)) s = `'${s}`;
  return /[",\r\n]/.test(s) ? `"${s.replaceAll('"', '""')}"` : s;
}

/** Amounts as unquoted numbers with two decimals (e.g. 1234.50), so spreadsheets treat them as numbers. */
const amount = (pence) => (pence == null ? '' : new Num(penceToInput(pence)));
const ukDateTime = (d) =>
  d.toLocaleString('en-GB', { dateStyle: 'short', timeStyle: 'short', timeZone: 'Europe/London' });

function payoffText(payoff) {
  if (payoff.status === 'clear') return 'Paid off';
  if (payoff.status === 'never') return 'Never at this payment';
  return `${MONTH_NAMES[payoff.month - 1]} ${payoff.year} (${payoff.months} months)`;
}

/** One month as a spreadsheet-friendly CSV: a block per section, amounts as plain numbers. */
export function monthToCsv(data, now = new Date()) {
  const { month: m, summary: s } = data;
  const rows = [
    ['Household Budget', m.label],
    ['Exported', ukDateTime(now)],
    [],
    ['Summary', 'Amount (GBP)'],
    ['Total income', amount(s.totalIncome)],
    ['Total fixed bills', amount(s.totalBills)],
    ['Total savings contributions', amount(s.totalContributions)],
    ['Total debt payments', amount(s.totalDebtPayments)],
    ['Left over', amount(s.leftOver)],
    ['Total savings (all pots)', amount(s.totalSavings)],
    ['Total remaining debt', amount(s.totalDebt)],
    [],
    ['Income'],
    ['Source', 'Recurring', 'Amount'],
    ...data.income.map((i) => [i.name, i.recurring ? 'Monthly' : 'One-off', amount(i.amount)]),
    [],
    ['Fixed bills'],
    ['Bill', 'Category', 'Due date', 'Amount'],
    ...data.bills.map((b) => [b.name, b.category ?? '', dueDateLabel(m.year, m.month, b.dueDay), amount(b.amount)]),
    [],
    ['Savings pots'],
    ['Pot', 'Opening balance', 'Contribution', 'Withdrawn', 'Closing balance', 'Target', 'Progress %'],
    ...data.pots.map((p) => [
      p.name, amount(p.opening), amount(p.contribution), amount(p.withdrawal), amount(p.closing),
      amount(p.target), p.progress == null ? '' : Math.round(p.progress * 1000) / 10,
    ]),
    [],
    ['Debts'],
    ['Debt', 'Opening balance', 'APR %', 'Interest', 'Payment', 'Closing balance', 'Estimated payoff'],
    ...data.debts.map((d) => [
      d.name, amount(d.opening), d.apr ?? '', amount(d.interest), amount(d.payment), amount(d.closing), payoffText(d.payoff),
    ]),
  ];
  if (m.notes) rows.push([], ['Notes'], [m.notes]);
  // BOM so Excel reads UTF-8 (the £ sign, accents) correctly.
  return '﻿' + rows.map((r) => r.map(csvCell).join(',')).join('\r\n') + '\r\n';
}

export function csvFilename(month) {
  return `budget-${monthKey(month.year, month.month)}.csv`;
}

// ---------------------------------------------------------------- backup

export const BACKUP_FORMAT = 1;

// Stored fields only: everything computed (closing balances, summaries) is rebuilt on restore.
const STORED = {
  income: ['name', 'amount', 'recurring'],
  bills: ['name', 'amount', 'category', 'dueDay'],
  pots: ['seriesId', 'name', 'target', 'opening', 'contribution', 'withdrawal'],
  debts: ['seriesId', 'name', 'opening', 'payment', 'apr'],
};
const pick = (obj, keys) => Object.fromEntries(keys.map((k) => [k, obj[k]]));

export function buildBackup(db, now = new Date()) {
  return {
    app: 'household-budget',
    format: BACKUP_FORMAT,
    schemaVersion: SCHEMA_VERSION,
    exportedAt: now.toISOString(),
    months: repo.allMonthsDetailed(db).map((m) => ({
      year: m.month.year,
      month: m.month.month,
      notes: m.month.notes,
      createdAt: m.month.createdAt,
      updatedAt: m.month.updatedAt,
      ...Object.fromEntries(SECTION_NAMES.map((s) => [s, m[s].map((it) => pick(it, STORED[s]))])),
    })),
  };
}

export function backupFilename(now = new Date()) {
  return `budget-backup-${now.toISOString().slice(0, 10)}.json`;
}

const SECTION_TITLES = { income: 'Income', bills: 'Fixed bills', pots: 'Savings pots', debts: 'Debts' };
const ISO = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?Z$/;

/**
 * Check a backup completely before touching the database. Returns normalised months or
 * throws a 400 describing the first problem found (month, section and item named).
 */
export function parseBackup(data) {
  const bad = (msg) => { throw new AppError(400, `This backup can't be restored: ${msg}`); };
  if (data?.app !== 'household-budget' || !Array.isArray(data.months)) bad("it isn't a Household Budget backup file.");
  if (data.format !== BACKUP_FORMAT) bad(`it uses backup format ${data.format}, which this version doesn't understand.`);

  const seen = new Set();
  return data.months.map((raw, i) => {
    const mv = validateMonth(raw);
    if (!mv.ok) bad(`month #${i + 1} has an invalid year or month.`);
    const { year, month } = mv.value;
    const label = monthLabel(year, month);
    const key = monthKey(year, month);
    if (seen.has(key)) bad(`${label} appears twice.`);
    seen.add(key);
    if (raw.notes != null && (typeof raw.notes !== 'string' || raw.notes.length > 5000)) bad(`${label} has invalid notes.`);

    const out = {
      year, month, label,
      notes: raw.notes ?? '',
      createdAt: ISO.test(raw.createdAt ?? '') ? raw.createdAt : null,
      updatedAt: ISO.test(raw.updatedAt ?? '') ? raw.updatedAt : null,
    };
    for (const section of SECTION_NAMES) {
      const items = raw[section] ?? [];
      if (!Array.isArray(items)) bad(`${label} → ${SECTION_TITLES[section]} isn't a list.`);
      const series = new Set();
      out[section] = items.map((item, j) => {
        const v = validateItem(section, item);
        if (!v.ok) {
          const [field, msg] = Object.entries(v.errors)[0];
          bad(`${label} → ${SECTION_TITLES[section]} → "${item?.name ?? `item #${j + 1}`}": ${field} — ${msg}.`);
        }
        if (STORED[section].includes('seriesId')) {
          const sid = typeof item.seriesId === 'string' && item.seriesId.length > 0 && item.seriesId.length <= 100
            ? item.seriesId : randomUUID();
          if (series.has(sid)) bad(`${label} → ${SECTION_TITLES[section]} has two items with the same id.`);
          series.add(sid);
          return { ...v.value, seriesId: sid };
        }
        return v.value;
      });
    }
    return out;
  });
}

/** Replace all data with a parsed backup, in one transaction. */
export function restoreBackup(db, months) {
  return transaction(db, () => {
    db.exec('DELETE FROM months'); // cascades to every item table
    const setTimes = db.prepare(
      `UPDATE months SET created_at = COALESCE(?, created_at), updated_at = COALESCE(?, updated_at) WHERE id = ?`,
    );
    for (const m of months) {
      const id = repo.createMonth(db, { year: m.year, month: m.month, notes: m.notes });
      for (const section of SECTION_NAMES) {
        for (const { seriesId, ...item } of m[section]) repo.addItem(db, section, id, item, { seriesId });
      }
      setTimes.run(m.createdAt, m.updatedAt, id);
    }
    return { months: months.length };
  });
}
