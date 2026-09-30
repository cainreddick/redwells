// Entry point: `npm start`. Opens the database, snapshots it, serves the app on
// localhost and opens the browser.

import path from 'node:path';
import fs from 'node:fs';
import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { openDatabase, snapshot } from './db.js';
import { createServer } from './app.js';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const DB_FILE = path.resolve(process.env.BUDGET_DB ?? path.join(ROOT, 'data', 'budget.db'));
const BACKUP_DIR = path.resolve(process.env.BUDGET_BACKUP_DIR ?? path.join(path.dirname(DB_FILE), 'backups'));
const PORT = Number(process.env.PORT ?? 4600);
const HOST = '127.0.0.1';
const OPEN_BROWSER = !process.argv.includes('--no-open') && !process.env.BUDGET_NO_OPEN;
const URL = `http://localhost:${PORT}`;

function openBrowser(url) {
  const [cmd, args] =
    process.platform === 'darwin' ? ['open', [url]]
    : process.platform === 'win32' ? ['cmd', ['/c', 'start', '', url]]
    : ['xdg-open', [url]];
  const child = spawn(cmd, args, { stdio: 'ignore', detached: true });
  child.on('error', () => console.log(`Open ${url} in your browser.`));
  child.unref();
}

const existed = fs.existsSync(DB_FILE);
const db = openDatabase(DB_FILE);

if (existed && db.prepare('SELECT COUNT(*) AS n FROM months').get().n > 0) {
  const file = snapshot(db, BACKUP_DIR, { prefix: 'startup', keep: 10 });
  console.log(`Snapshot saved: ${file}`);
}

const server = createServer({ db });

server.on('error', (err) => {
  if (err.code === 'EADDRINUSE') {
    console.error(`Port ${PORT} is already in use — is the budget app already running? Try ${URL}`);
    console.error('Or start on another port: PORT=4601 npm start');
  } else {
    console.error(err);
  }
  process.exit(1);
});

server.listen(PORT, HOST, () => {
  console.log(`Household budget running at ${URL}`);
  console.log(`Data file: ${DB_FILE}`);
  console.log('Press Ctrl+C to stop.');
  if (OPEN_BROWSER) openBrowser(URL);
});

function shutdown() {
  server.close();
  db.close();
  process.exit(0);
}
process.on('SIGINT', shutdown);
process.on('SIGTERM', shutdown);
