# Household Budget

A local, single-computer budget planner. Each month is its own saved record with income,
fixed bills, savings pots and debts. Everything is stored in **one SQLite file** on disk.
There are no accounts and no cloud, and nothing lives in browser storage.

> **Status:** stage 1 of 6 is done (data model, storage and JSON API). The user interface
> arrives in stage 2.

## Requirements

- **Node.js 22.13 or newer** (22 LTS or 24 LTS). Check with `node --version`.
  Nothing else is needed: SQLite is built into Node, and there are no npm dependencies to install.

## Run it

```sh
npm start
```

This starts the app at <http://localhost:4600> and opens your browser. Press `Ctrl+C` in the
terminal to stop it. The server listens on `127.0.0.1` only, so other devices on your network
can't reach it.

| Setting | How | Default |
|---|---|---|
| Data file location | `BUDGET_DB=/path/to/budget.db npm start` | `./data/budget.db` |
| Automatic snapshot folder | `BUDGET_BACKUP_DIR=/path npm start` | `backups/` next to the data file |
| Port | `PORT=4601 npm start` | `4600` |
| Don't open a browser | `npm start -- --no-open` | opens one |

Other scripts:

- `npm test` runs the test suite (Node's built-in test runner).
- `npm run dev` restarts the server automatically when you edit the code.

## Your data and backups

All data lives in **`data/budget.db`** (or wherever `BUDGET_DB` points).

- **Manual backup:** copy `budget.db` somewhere safe. The database uses SQLite's rollback
  journal rather than WAL, so that one file is always the complete dataset. There are no
  `-wal` or `-shm` side files to forget. Copy it while the app is stopped, or at least not
  mid-save.
- **Automatic snapshots:** every time the app starts with existing data, it writes a
  consistent copy to `data/backups/startup-<date>_<time>.db` and keeps the 10 most recent.
- **Restore from a `.db` file:** stop the app, replace `data/budget.db` with your copy, then
  start it again.
- **Keep it somewhere synced:** point `BUDGET_DB` at a folder that Dropbox, OneDrive or
  iCloud syncs (for example `BUDGET_DB=~/Dropbox/budget/budget.db npm start`) to get
  off-machine backups for free. Only run one copy of the app against the file at a time.

`data/` is in `.gitignore`, so your figures never end up in git.

## How the numbers work

- Money is stored as whole **pence** (integers), so no floating-point rounding creeps in.
  It's displayed as GBP (`£1,234.56`), and dates use the UK format (`DD/MM/YYYY`).
- **Savings pot:** closing balance = opening + contribution − withdrawals. Progress is
  closing ÷ target. The target is optional; with no target, no progress is shown.
- **Debt:** monthly interest = opening balance × APR ÷ 12, rounded to the penny.
  Closing balance = opening + interest − payment. This is an estimate, because most real
  lenders calculate interest daily.
- **Payoff date:** worked out month by month from the current balance, payment and APR. It
  shows "never" if the payment doesn't cover the interest.
- **Left over** = income − bills − savings contributions − debt payments.

## Project layout

```
server/
  index.js   entry point: opens the DB, takes a snapshot, starts HTTP, opens the browser
  app.js     HTTP server: JSON API router and static file serving
  repo.js    all SQL; converts rows <-> API objects
  db.js      opening the DB, schema migrations, transactions, snapshots
shared/      pure JS used by both server and browser
  calc.js    money parsing and formatting, dates, pot/debt/summary maths
  validate.js per-section field validation
public/      the browser app (static files, no build step)
test/        node:test suites
```

## API (for reference)

All amounts are integer pence in responses. Requests may send pence integers or pound
strings such as `"1,234.56"`. Every item change returns the whole refreshed month, including
its `summary`.

| Method | Path | Body |
|---|---|---|
| GET | `/api/months` | (lists months, newest first, each with a summary) |
| POST | `/api/months` | `{ year, month }` |
| GET | `/api/months/:id` | (the month with its items and computed fields) |
| PATCH | `/api/months/:id` | `{ notes }` |
| DELETE | `/api/months/:id` | (deletes the month and all its items) |
| POST | `/api/months/:id/:section` | item fields (section is `income`, `bills`, `pots` or `debts`) |
| PUT | `/api/:section/:itemId` | all item fields |
| DELETE | `/api/:section/:itemId` | |

Item fields:

- **income:** `name`, `amount`, `recurring` (bool, default true; only recurring income is
  copied forward)
- **bills:** `name`, `amount`, `category` (optional, from a fixed list), `dueDay` (optional,
  1–31)
- **pots:** `name`, `target` (optional), `opening`, `contribution`, `withdrawal`
- **debts:** `name`, `opening`, `payment`, `apr` (optional, a percentage)

Validation failures return `400` with `{ error, details: { field: message } }`.
