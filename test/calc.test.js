import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  addMonths, debtFigures, dueDateLabel, enrichMonth, formatGBP, monthLabel, monthlyInterest,
  parsePounds, payoffMonths, penceToInput, potFigures,
} from '../shared/calc.js';

test('parsePounds handles common input and rejects junk', () => {
  assert.equal(parsePounds('1234.56'), 123456);
  assert.equal(parsePounds('£1,234.5'), 123450);
  assert.equal(parsePounds(' 12 '), 1200);
  assert.equal(parsePounds('.5'), 50);
  assert.equal(parsePounds('0.29'), 29); // no float drift
  assert.equal(parsePounds('-3.20'), -320);
  assert.equal(parsePounds(12.3), 1230);
  for (const bad of ['', 'abc', '1.234', '1..2', '£', '1e5', null, undefined]) {
    assert.equal(parsePounds(bad), null, `expected null for ${bad}`);
  }
});

test('formatGBP and penceToInput', () => {
  assert.equal(formatGBP(123456), '£1,234.56');
  assert.equal(formatGBP(-500), '-£5.00');
  assert.equal(penceToInput(123405), '1234.05');
  assert.equal(penceToInput(0), '0.00');
  assert.equal(penceToInput(null), '');
});

test('month arithmetic and UK dates', () => {
  assert.equal(monthLabel(2026, 10), 'October 2026');
  assert.deepEqual(addMonths(2026, 11, 3), { year: 2027, month: 2 });
  assert.deepEqual(addMonths(2026, 1, -1), { year: 2025, month: 12 });
  assert.equal(dueDateLabel(2026, 2, 31), '28/02/2026');
  assert.equal(dueDateLabel(2028, 2, 30), '29/02/2028');
  assert.equal(dueDateLabel(2026, 10, 5), '05/10/2026');
  assert.equal(dueDateLabel(2026, 10, null), '');
});

test('potFigures: closing balance and progress', () => {
  const f = potFigures({ opening: 100000, contribution: 20000, withdrawal: 5000, target: 500000 });
  assert.equal(f.closing, 115000);
  assert.equal(f.progress, 0.23);
  assert.equal(f.remaining, 385000);
  const noTarget = potFigures({ opening: 0, contribution: 100, withdrawal: 0, target: null });
  assert.equal(noTarget.progress, null);
});

test('monthlyInterest is APR/12 on the balance, rounded to the penny', () => {
  assert.equal(monthlyInterest(120000, 12), 1200); // £1200 @ 12% → £12
  assert.equal(monthlyInterest(100000, 19.9), 1658); // 1658.33…
  assert.equal(monthlyInterest(100000, null), 0);
  assert.equal(monthlyInterest(0, 20), 0);
});

test('payoffMonths without interest', () => {
  assert.equal(payoffMonths(100000, 25000, null), 4);
  assert.equal(payoffMonths(100000, 30000, 0), 4); // 3 × £300 + £100 final
  assert.equal(payoffMonths(0, 1000, 10), 0);
  assert.equal(payoffMonths(100000, 0, null), Infinity);
});

test('payoffMonths with interest', () => {
  // £1,000 at 12% APR paying £100/month → 11 months (standard amortisation gives 10.6)
  assert.equal(payoffMonths(100000, 10000, 12), 11);
  // Payment only covers interest → never
  assert.equal(payoffMonths(100000, 1000, 12), Infinity);
});

test('debtFigures: closing balance and payoff date', () => {
  const d = debtFigures({ opening: 100000, payment: 10000, apr: 12 }, 2026, 10);
  assert.equal(d.interest, 1000);
  assert.equal(d.closing, 91000);
  assert.deepEqual(d.payoff, { status: 'ok', months: 11, year: 2027, month: 8 });

  const final = debtFigures({ opening: 5000, payment: 5000, apr: null }, 2026, 10);
  assert.equal(final.closing, 0);
  assert.deepEqual(final.payoff, { status: 'ok', months: 1, year: 2026, month: 10 });

  assert.equal(debtFigures({ opening: 100000, payment: 500, apr: 12 }, 2026, 10).payoff.status, 'never');
  assert.equal(debtFigures({ opening: 0, payment: 0, apr: null }, 2026, 10).payoff.status, 'clear');
});

test('enrichMonth summary totals', () => {
  const data = enrichMonth({
    month: { year: 2026, month: 10 },
    income: [{ amount: 250000 }, { amount: 180000 }],
    bills: [{ amount: 120000 }, { amount: 15000 }],
    pots: [
      { opening: 100000, contribution: 20000, withdrawal: 0, target: null },
      { opening: 50000, contribution: 10000, withdrawal: 5000, target: 100000 },
    ],
    debts: [{ opening: 100000, payment: 10000, apr: 12 }],
  });
  assert.deepEqual(data.summary, {
    totalIncome: 430000,
    totalBills: 135000,
    totalContributions: 30000,
    totalWithdrawals: 5000,
    totalDebtPayments: 10000,
    leftOver: 255000,
    totalSavings: 175000,
    totalDebt: 91000,
  });
  assert.equal(data.pots[1].closing, 55000);
  assert.equal(data.debts[0].closing, 91000);
});

test('leftOver goes negative when overspent', () => {
  const { summary } = enrichMonth({
    month: { year: 2026, month: 10 },
    income: [{ amount: 1000 }],
    bills: [{ amount: 2000 }],
    pots: [],
    debts: [],
  });
  assert.equal(summary.leftOver, -1000);
});
