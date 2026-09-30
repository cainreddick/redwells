import { test, before, after } from 'node:test';
import assert from 'node:assert/strict';
import { openDatabase } from '../server/db.js';
import { createServer } from '../server/app.js';

let server;
let base;

before(async () => {
  server = createServer({ db: openDatabase(':memory:'), log: { error() {} } });
  await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
  base = `http://localhost:${server.address().port}`;
});

after(() => server.close());

async function api(method, path, body, headers = {}) {
  const res = await fetch(base + path, {
    method,
    headers: body === undefined ? headers : { 'Content-Type': 'application/json', ...headers },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  return { status: res.status, body: await res.json() };
}

test('health check', async () => {
  const r = await api('GET', '/api/health');
  assert.equal(r.status, 200);
  assert.equal(r.body.ok, true);
});

test('month and item lifecycle over HTTP', async () => {
  const created = await api('POST', '/api/months', { year: 2026, month: 10 });
  assert.equal(created.status, 201);
  assert.equal(created.body.month.label, 'October 2026');
  const id = created.body.month.id;

  assert.equal((await api('POST', '/api/months', { year: 2026, month: 10 })).status, 409);

  let r = await api('POST', `/api/months/${id}/income`, { name: 'Salary', amount: '2500' });
  assert.equal(r.status, 201);
  assert.equal(r.body.summary.totalIncome, 250000);
  const incomeId = r.body.income[0].id;

  r = await api('PUT', `/api/income/${incomeId}`, { name: 'Salary', amount: 260000, recurring: true });
  assert.equal(r.body.income[0].amount, 260000);

  r = await api('POST', `/api/months/${id}/debts`, { name: 'Card', opening: '1000', payment: '100', apr: '12' });
  assert.equal(r.body.debts[0].closing, 91000);
  assert.equal(r.body.summary.leftOver, 260000 - 10000);

  r = await api('PATCH', `/api/months/${id}`, { notes: 'Car MOT due' });
  assert.equal(r.body.month.notes, 'Car MOT due');

  r = await api('GET', '/api/months');
  assert.equal(r.body.length, 1);
  assert.equal(r.body[0].summary.totalDebt, 91000);

  r = await api('DELETE', `/api/income/${incomeId}`);
  assert.equal(r.body.income.length, 0);

  assert.equal((await api('DELETE', `/api/months/${id}`)).status, 200);
  assert.equal((await api('GET', `/api/months/${id}`)).status, 404);
});

test('validation errors come back per field', async () => {
  const { body: m } = await api('POST', '/api/months', { year: 2026, month: 11 });
  const r = await api('POST', `/api/months/${m.month.id}/bills`, { name: '', amount: '-1', dueDay: 40 });
  assert.equal(r.status, 400);
  assert.deepEqual(Object.keys(r.body.details).sort(), ['amount', 'dueDay', 'name']);
  assert.equal((await api('POST', '/api/months', { year: 1999, month: 1 })).status, 400);
});

test('unknown routes and sections', async () => {
  assert.equal((await api('GET', '/api/nope')).status, 404);
  assert.equal((await api('PUT', '/api/cats/1', {})).status, 404);
  assert.equal((await api('PATCH', '/api/months')).status, 405);
});

test('rejects cross-origin and non-JSON writes', async () => {
  const cross = await api('POST', '/api/months', { year: 2027, month: 1 }, { Origin: 'https://evil.example' });
  assert.equal(cross.status, 403);

  const form = await fetch(`${base}/api/months`, {
    method: 'POST',
    headers: { 'Content-Type': 'text/plain' },
    body: JSON.stringify({ year: 2027, month: 1 }),
  });
  assert.equal(form.status, 415);
});

test('serves static files without path traversal', async () => {
  const index = await fetch(`${base}/`);
  assert.equal(index.status, 200);
  assert.match(index.headers.get('content-type'), /text\/html/);
  const shared = await fetch(`${base}/shared/calc.js`);
  assert.equal(shared.status, 200);
  const traversal = await fetch(`${base}/shared/..%2F..%2Fpackage.json`);
  assert.equal(traversal.status, 404);
});

test('POST /api/months with copyFrom returns the new month and a copy report', async () => {
  const { body: src } = await api('POST', '/api/months', { year: 2030, month: 1 });
  await api('POST', `/api/months/${src.month.id}/pots`, { name: 'Rainy day', opening: '100', contribution: '50' });
  const r = await api('POST', '/api/months', { year: 2030, month: 2, copyFrom: src.month.id });
  assert.equal(r.status, 201);
  assert.equal(r.body.pots[0].opening, 15000);
  assert.equal(r.body.copyReport.source, 'January 2030');
  assert.equal((await api('POST', '/api/months', { year: 2029, month: 12, copyFrom: src.month.id })).status, 400);
});

test('PUT re-validates the whole item, including cross-field rules', async () => {
  const { body: m } = await api('POST', '/api/months', { year: 2031, month: 5 });
  const { body: withPot } = await api('POST', `/api/months/${m.month.id}/pots`, { name: 'Car', opening: '100' });
  const pot = withPot.pots[0];
  const r = await api('PUT', `/api/pots/${pot.id}`, { ...pot, withdrawal: 20000 });
  assert.equal(r.status, 400);
  assert.match(r.body.details.withdrawal, /Cannot withdraw more/);
  const ok = await api('PUT', `/api/pots/${pot.id}`, { ...pot, name: 'Car fund', withdrawal: 2500 });
  assert.equal(ok.body.pots[0].closing, 7500);
  assert.equal(ok.body.pots[0].seriesId, pot.seriesId); // edits keep the series link
});
