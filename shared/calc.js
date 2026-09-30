// Money, date and budget calculations shared by the server and the browser.
// All money values are integer pence. Nothing in here touches I/O.

export const MAX_PENCE = 10_000_000_000; // £100,000,000 — a sanity cap, not a business rule
const MAX_PAYOFF_MONTHS = 1200; // 100 years; beyond this we report "never"

export const MONTH_NAMES = [
  'January', 'February', 'March', 'April', 'May', 'June',
  'July', 'August', 'September', 'October', 'November', 'December',
];

// ---------------------------------------------------------------- money

const GBP = new Intl.NumberFormat('en-GB', { style: 'currency', currency: 'GBP' });

export function formatGBP(pence) {
  return GBP.format((pence ?? 0) / 100);
}

/** Pence → plain "1234.56" for input fields (no symbol or separators). */
export function penceToInput(pence) {
  if (pence == null) return '';
  const sign = pence < 0 ? '-' : '';
  const abs = Math.abs(pence);
  return `${sign}${Math.floor(abs / 100)}.${String(abs % 100).padStart(2, '0')}`;
}

/**
 * Parse user-entered pounds ("£1,234.5", "12", "-3.20") into integer pence.
 * Returns null for anything that isn't a plain amount with at most 2 decimal places.
 * Parsing is done on the string so that "0.29" never becomes 28.999… pence.
 */
export function parsePounds(input) {
  if (typeof input === 'number') input = String(input);
  if (typeof input !== 'string') return null;
  const s = input.trim().replace(/^£/, '').replace(/,/g, '');
  const m = /^(-)?(\d+)(?:\.(\d{1,2}))?$/.exec(s) ?? /^(-)?()\.(\d{1,2})$/.exec(s);
  if (!m) return null;
  const [, neg, whole, frac = ''] = m;
  const pence = Number(whole || 0) * 100 + Number(frac.padEnd(2, '0'));
  return neg ? -pence : pence;
}

// ---------------------------------------------------------------- dates

export function monthLabel(year, month) {
  return `${MONTH_NAMES[month - 1]} ${year}`;
}

export function monthKey(year, month) {
  return `${year}-${String(month).padStart(2, '0')}`;
}

export function parseMonthKey(key) {
  const m = /^(\d{4})-(\d{2})$/.exec(key ?? '');
  if (!m) return null;
  const year = Number(m[1]);
  const month = Number(m[2]);
  return month >= 1 && month <= 12 ? { year, month } : null;
}

export function addMonths(year, month, n) {
  const idx = year * 12 + (month - 1) + n;
  return { year: Math.floor(idx / 12), month: (idx % 12) + 1 };
}

export function daysInMonth(year, month) {
  return new Date(Date.UTC(year, month, 0)).getUTCDate();
}

export function formatDateUK(year, month, day) {
  return `${String(day).padStart(2, '0')}/${String(month).padStart(2, '0')}/${year}`;
}

/** A bill due on the 31st falls on the last day of shorter months. */
export function dueDateLabel(year, month, dueDay) {
  if (!dueDay) return '';
  return formatDateUK(year, month, Math.min(dueDay, daysInMonth(year, month)));
}

// ---------------------------------------------------------------- savings

export function potFigures(pot) {
  const closing = pot.opening + pot.contribution - pot.withdrawal;
  const hasTarget = pot.target != null && pot.target > 0;
  return {
    closing,
    progress: hasTarget ? closing / pot.target : null, // fraction, may exceed 1
    remaining: hasTarget ? Math.max(0, pot.target - closing) : null,
  };
}

// ---------------------------------------------------------------- debts

/** Interest for one month: APR ÷ 12 applied to the balance, rounded to the penny. */
export function monthlyInterest(balance, apr) {
  if (!apr || balance <= 0) return 0;
  return Math.round((balance * apr) / 100 / 12);
}

/**
 * How many months (counting the current one as 1) until the balance hits zero.
 * 0 if already clear; Infinity if the payment never covers the interest.
 */
export function payoffMonths(opening, payment, apr) {
  let balance = opening;
  if (balance <= 0) return 0;
  for (let n = 1; n <= MAX_PAYOFF_MONTHS; n++) {
    const interest = monthlyInterest(balance, apr);
    balance += interest;
    if (payment >= balance) return n;
    if (payment <= interest) return Infinity;
    balance -= payment;
  }
  return Infinity;
}

export function debtFigures(debt, year, month) {
  const interest = monthlyInterest(debt.opening, debt.apr);
  const closing = Math.max(0, debt.opening + interest - debt.payment);
  const months = payoffMonths(debt.opening, debt.payment, debt.apr);
  let payoff = null;
  if (months === 0) payoff = { status: 'clear' };
  else if (months === Infinity) payoff = { status: 'never' };
  else payoff = { status: 'ok', months, ...addMonths(year, month, months - 1) };
  return { interest, closing, payoff };
}

// ---------------------------------------------------------------- summary

const sum = (items, fn) => items.reduce((acc, it) => acc + fn(it), 0);

/** Adds computed fields to pots and debts, and a summary block, to a month payload. */
export function enrichMonth(data) {
  const { year, month } = data.month;
  const pots = data.pots.map((p) => ({ ...p, ...potFigures(p) }));
  const debts = data.debts.map((d) => ({ ...d, ...debtFigures(d, year, month) }));
  return { ...data, pots, debts, summary: summarise({ ...data, pots, debts }) };
}

export function summarise({ income, bills, pots, debts }) {
  const totalIncome = sum(income, (i) => i.amount);
  const totalBills = sum(bills, (b) => b.amount);
  const totalContributions = sum(pots, (p) => p.contribution);
  const totalWithdrawals = sum(pots, (p) => p.withdrawal);
  const totalDebtPayments = sum(debts, (d) => d.payment);
  return {
    totalIncome,
    totalBills,
    totalContributions,
    totalWithdrawals,
    totalDebtPayments,
    leftOver: totalIncome - totalBills - totalContributions - totalDebtPayments,
    totalSavings: sum(pots, (p) => potFigures(p).closing),
    totalDebt: sum(debts, (d) => Math.max(0, d.opening + monthlyInterest(d.opening, d.apr) - d.payment)),
  };
}
