import { test } from 'node:test';
import assert from 'node:assert/strict';
import { validateItem, validateMonth } from '../shared/validate.js';

test('income: required fields and normalisation', () => {
  assert.deepEqual(validateItem('income', { name: '  Salary ', amount: '2,500.00' }), {
    ok: true,
    value: { name: 'Salary', amount: 250000, recurring: true },
  });
  const r = validateItem('income', { name: '', amount: '-5' });
  assert.equal(r.ok, false);
  assert.equal(r.errors.name, 'Required');
  assert.equal(r.errors.amount, 'Cannot be negative');
});

test('amounts: accepts pence integers, rejects fractional pence and huge values', () => {
  assert.equal(validateItem('income', { name: 'x', amount: 123 }).value.amount, 123);
  assert.equal(validateItem('income', { name: 'x', amount: 1.5 }).ok, false);
  assert.equal(validateItem('income', { name: 'x', amount: '1.234' }).ok, false);
  assert.equal(validateItem('income', { name: 'x', amount: 1e15 }).ok, false);
});

test('bills: optional category and due day', () => {
  assert.deepEqual(validateItem('bills', { name: 'Rent', amount: 100000 }).value, {
    name: 'Rent', amount: 100000, category: null, dueDay: null,
  });
  const ok = validateItem('bills', { name: 'Rent', amount: 1, category: 'Housing', dueDay: '28' });
  assert.equal(ok.value.dueDay, 28);
  const bad = validateItem('bills', { name: 'Rent', amount: 1, category: 'Nope', dueDay: 32 });
  assert.deepEqual(Object.keys(bad.errors).sort(), ['category', 'dueDay']);
});

test('pots: defaults to zero and blocks over-withdrawal', () => {
  assert.deepEqual(validateItem('pots', { name: 'Holiday' }).value, {
    name: 'Holiday', target: null, opening: 0, contribution: 0, withdrawal: 0,
  });
  const r = validateItem('pots', { name: 'Holiday', opening: 1000, contribution: 500, withdrawal: 2000 });
  assert.equal(r.ok, false);
  assert.match(r.errors.withdrawal, /£15\.00/);
});

test('debts: APR range and payment cap', () => {
  assert.equal(validateItem('debts', { name: 'Card', opening: 100000, payment: 5000, apr: '19.9%' }).value.apr, 19.9);
  assert.equal(validateItem('debts', { name: 'Card', opening: 1, apr: 101 }).errors.apr, 'At most 100%');
  assert.equal(validateItem('debts', { name: 'Card', opening: 1, apr: -1 }).errors.apr, 'Cannot be negative');
  // £1,000 at 12% → £1,010 owed; paying £1,010 is fine, £1,010.01 is not
  assert.equal(validateItem('debts', { name: 'Card', opening: 100000, payment: 101000, apr: 12 }).ok, true);
  assert.equal(validateItem('debts', { name: 'Card', opening: 100000, payment: 101001, apr: 12 }).ok, false);
  assert.equal(validateItem('debts', { name: 'Card' }).errors.opening, 'Required');
});

test('validateMonth', () => {
  assert.deepEqual(validateMonth({ year: '2026', month: 10 }), { ok: true, value: { year: 2026, month: 10 } });
  assert.equal(validateMonth({ year: 2026, month: 13 }).ok, false);
  assert.equal(validateMonth({}).ok, false);
});
