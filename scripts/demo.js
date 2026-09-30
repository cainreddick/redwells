// `npm run demo` — runs the app against a separate demo database (data/demo.db) filled with
// sample months, so you can try things without touching your real data.
// `npm run demo -- --reset` rebuilds the demo data from scratch.

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { openDatabase } from '../server/db.js';
import * as repo from '../server/repo.js';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const DEMO_DB = path.join(ROOT, 'data', 'demo.db');

function seed(file) {
  const db = openDatabase(file);
  const july = repo.createMonth(db, { year: 2026, month: 7, notes: 'Sample data. Edit freely.' });
  const add = (section, item) => repo.addItem(db, section, july, item);

  add('income', { name: 'Salary (me)', amount: 285000, recurring: true });
  add('income', { name: 'Salary (wife)', amount: 231000, recurring: true });
  add('income', { name: 'Business drawings', amount: 60000, recurring: true });
  add('income', { name: 'Tax refund', amount: 18750, recurring: false });

  add('bills', { name: 'Mortgage', amount: 142500, category: 'Housing', dueDay: 1 });
  add('bills', { name: 'Council tax', amount: 21300, category: 'Council tax', dueDay: 1 });
  add('bills', { name: 'Energy', amount: 16500, category: 'Utilities', dueDay: 15 });
  add('bills', { name: 'Water', amount: 4200, category: 'Utilities', dueDay: 20 });
  add('bills', { name: 'Broadband', amount: 3500, category: 'Phone & internet', dueDay: 8 });
  add('bills', { name: 'Car insurance', amount: 6400, category: 'Insurance', dueDay: 28 });
  add('bills', { name: 'Streaming', amount: 2799, category: 'Subscriptions', dueDay: 12 });
  add('bills', { name: 'Groceries', amount: 60000, category: 'Groceries', dueDay: null });

  add('pots', { name: 'Emergency fund', target: 1000000, opening: 620000, contribution: 30000, withdrawal: 0 });
  add('pots', { name: 'Holiday', target: 250000, opening: 90000, contribution: 20000, withdrawal: 0 });
  add('pots', { name: 'Christmas', target: 80000, opening: 35000, contribution: 7500, withdrawal: 0 });

  add('debts', { name: 'Credit card', opening: 184000, payment: 20000, apr: 22.9 });
  add('debts', { name: 'Car finance', opening: 742000, payment: 28500, apr: 6.9 });
  add('debts', { name: 'Loan from family', opening: 30000, payment: 15000, apr: null });

  const aug = repo.createMonthFromCopy(db, july, { year: 2026, month: 8 }).monthId;
  const hol = repo.getMonth(db, aug).pots.find((p) => p.name === 'Holiday');
  repo.updateItem(db, 'pots', hol.id, { ...hol, withdrawal: 60000 });
  repo.createMonthFromCopy(db, aug, { year: 2026, month: 9 });
  db.close();
}

if (process.argv.includes('--reset')) fs.rmSync(DEMO_DB, { force: true });
if (!fs.existsSync(DEMO_DB)) {
  seed(DEMO_DB);
  console.log(`Demo data created in ${DEMO_DB}`);
}

process.env.BUDGET_DB = DEMO_DB;
process.env.BUDGET_BACKUP_DIR ??= path.join(ROOT, 'data', 'demo-backups');
await import('../server/index.js');
