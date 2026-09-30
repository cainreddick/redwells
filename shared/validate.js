// Field-level validation for each tracked section, shared by the server (authoritative)
// and the browser (instant feedback). Input values may be pence integers or, from the
// browser, the raw strings typed into inputs; output values are always normalised.

import { MAX_PENCE, monthlyInterest, parsePounds, formatGBP } from './calc.js';

export const BILL_CATEGORIES = [
  'Housing', 'Council tax', 'Utilities', 'Phone & internet', 'Insurance',
  'Subscriptions', 'Transport', 'Childcare', 'Groceries', 'Health', 'Other',
];

export const SECTION_NAMES = ['income', 'bills', 'pots', 'debts'];

const blank = (v) => v === undefined || v === null || (typeof v === 'string' && v.trim() === '');

const field = {
  text: ({ max = 80 } = {}) => (v) => {
    if (blank(v)) return { error: 'Required' };
    if (typeof v !== 'string') return { error: 'Must be text' };
    const s = v.trim();
    if (s.length > max) return { error: `At most ${max} characters` };
    return { value: s };
  },

  pence: ({ required = true, fallback = null } = {}) => (v) => {
    if (blank(v)) return required ? { error: 'Required' } : { value: fallback };
    const p = typeof v === 'number' ? (Number.isSafeInteger(v) ? v : null) : parsePounds(v);
    if (p === null) return { error: 'Enter an amount like 1234.56' };
    if (p < 0) return { error: 'Cannot be negative' };
    if (p > MAX_PENCE) return { error: 'Amount is too large' };
    return { value: p };
  },

  bool: ({ fallback = true } = {}) => (v) => {
    if (blank(v)) return { value: fallback };
    if (typeof v === 'boolean') return { value: v };
    if (v === 1 || v === 0) return { value: v === 1 };
    return { error: 'Must be yes or no' };
  },

  choice: (options) => (v) => {
    if (blank(v)) return { value: null };
    return options.includes(v) ? { value: v } : { error: 'Pick one from the list' };
  },

  day: () => (v) => {
    if (blank(v)) return { value: null };
    const n = Number(v);
    if (!Number.isInteger(n) || n < 1 || n > 31) return { error: 'Day of month, 1–31' };
    return { value: n };
  },

  percent: () => (v) => {
    if (blank(v)) return { value: null };
    const n = typeof v === 'number' ? v : Number(String(v).trim().replace(/%$/, ''));
    if (!Number.isFinite(n)) return { error: 'Enter a percentage like 19.9' };
    if (n < 0) return { error: 'Cannot be negative' };
    if (n > 100) return { error: 'At most 100%' };
    return { value: Math.round(n * 100) / 100 };
  },
};

const SPECS = {
  income: {
    name: field.text(),
    amount: field.pence(),
    recurring: field.bool(),
  },
  bills: {
    name: field.text(),
    amount: field.pence(),
    category: field.choice(BILL_CATEGORIES),
    dueDay: field.day(),
  },
  pots: {
    name: field.text(),
    target: field.pence({ required: false }),
    opening: field.pence({ required: false, fallback: 0 }),
    contribution: field.pence({ required: false, fallback: 0 }),
    withdrawal: field.pence({ required: false, fallback: 0 }),
  },
  debts: {
    name: field.text(),
    opening: field.pence(),
    payment: field.pence({ required: false, fallback: 0 }),
    apr: field.percent(),
  },
};

const CROSS_CHECKS = {
  pots(v, errors) {
    if (v.withdrawal > v.opening + v.contribution) {
      errors.withdrawal = `Cannot withdraw more than the pot holds (${formatGBP(v.opening + v.contribution)})`;
    }
  },
  debts(v, errors) {
    const owed = v.opening + monthlyInterest(v.opening, v.apr);
    if (v.payment > owed) errors.payment = `More than is owed this month (${formatGBP(owed)})`;
  },
};

/**
 * Validate a whole item for a section. Unknown keys are ignored.
 * @returns {{ ok: true, value: object } | { ok: false, errors: Record<string,string> }}
 */
export function validateItem(section, input) {
  const spec = SPECS[section];
  if (!spec) throw new Error(`Unknown section: ${section}`);
  const value = {};
  const errors = {};
  for (const [key, check] of Object.entries(spec)) {
    const r = check(input?.[key]);
    if ('error' in r) errors[key] = r.error;
    else value[key] = r.value;
  }
  if (Object.keys(errors).length === 0) CROSS_CHECKS[section]?.(value, errors);
  return Object.keys(errors).length ? { ok: false, errors } : { ok: true, value };
}

export function validateMonth(input) {
  const errors = {};
  const year = Number(input?.year);
  const month = Number(input?.month);
  if (!Number.isInteger(year) || year < 2000 || year > 2100) errors.year = 'Year must be 2000–2100';
  if (!Number.isInteger(month) || month < 1 || month > 12) errors.month = 'Month must be 1–12';
  return Object.keys(errors).length ? { ok: false, errors } : { ok: true, value: { year, month } };
}
