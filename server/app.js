// HTTP layer: a tiny router for the JSON API plus static file serving.

import http from 'node:http';
import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import * as repo from './repo.js';
import { AppError } from './repo.js';
import { SCHEMA_VERSION } from './db.js';
import { SECTION_NAMES, validateItem, validateMonth } from '../shared/validate.js';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const STATIC_ROOTS = [
  { prefix: '/shared/', dir: path.join(ROOT, 'shared') },
  { prefix: '/', dir: path.join(ROOT, 'public') },
];
const MIME = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.mjs': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.svg': 'image/svg+xml',
  '.png': 'image/png',
  '.ico': 'image/x-icon',
};
const MAX_BODY_BYTES = 20 * 1024 * 1024;

// ---------------------------------------------------------------- routing

function route(method, pattern, handler) {
  const keys = [];
  const re = new RegExp(
    '^' + pattern.replace(/:(\w+)/g, (_, k) => (keys.push(k), '([^/]+)')) + '/?$',
  );
  return { method, re, keys, handler };
}

function id(value) {
  const n = Number(value);
  if (!Number.isSafeInteger(n) || n <= 0) throw new AppError(404, 'Not found');
  return n;
}

function sectionParam(value) {
  if (!SECTION_NAMES.includes(value)) throw new AppError(404, 'Not found');
  return value;
}

// Handlers return a plain payload (sent as 200) or created(payload) for 201.
const CREATED = Symbol('created');
const created = (payload) => ({ [CREATED]: true, payload });

function validated(result) {
  if (!result.ok) throw new AppError(400, 'Please fix the highlighted fields', result.errors);
  return result.value;
}

function buildRoutes(db) {
  return [
    route('GET', '/api/health', () => ({ ok: true, schemaVersion: SCHEMA_VERSION })),

    route('GET', '/api/months', () => repo.listMonths(db)),

    route('POST', '/api/months', ({ body }) => {
      const { year, month } = validated(validateMonth(body));
      const monthId = repo.createMonth(db, { year, month });
      return created(repo.getMonth(db, monthId));
    }),

    route('GET', '/api/months/:id', ({ params }) => repo.requireMonth(db, id(params.id))),

    route('PATCH', '/api/months/:id', ({ params, body }) => {
      const monthId = id(params.id);
      if (typeof body?.notes !== 'string') throw new AppError(400, 'notes must be a string');
      if (body.notes.length > 5000) throw new AppError(400, 'Notes are limited to 5000 characters');
      repo.updateMonthNotes(db, monthId, body.notes);
      return repo.getMonth(db, monthId);
    }),

    route('DELETE', '/api/months/:id', ({ params }) => {
      repo.deleteMonth(db, id(params.id));
      return { ok: true };
    }),

    // Item mutations return the whole refreshed month so the UI can redraw totals in one go.
    route('POST', '/api/months/:id/:section', ({ params, body }) => {
      const monthId = id(params.id);
      const sectionName = sectionParam(params.section);
      repo.addItem(db, sectionName, monthId, validated(validateItem(sectionName, body)));
      return created(repo.getMonth(db, monthId));
    }),

    route('PUT', '/api/:section/:id', ({ params, body }) => {
      const sectionName = sectionParam(params.section);
      const value = validated(validateItem(sectionName, body));
      return repo.getMonth(db, repo.updateItem(db, sectionName, id(params.id), value));
    }),

    route('DELETE', '/api/:section/:id', ({ params }) => {
      const sectionName = sectionParam(params.section);
      return repo.getMonth(db, repo.deleteItem(db, sectionName, id(params.id)));
    }),
  ];
}

// ---------------------------------------------------------------- helpers

function send(res, status, payload, headers = {}) {
  const body = typeof payload === 'string' || Buffer.isBuffer(payload) ? payload : JSON.stringify(payload);
  res.writeHead(status, {
    'Content-Type': 'application/json; charset=utf-8',
    'Cache-Control': 'no-store',
    'X-Content-Type-Options': 'nosniff',
    ...headers,
  });
  res.end(body);
}

async function readJson(req) {
  const chunks = [];
  let size = 0;
  for await (const chunk of req) {
    size += chunk.length;
    if (size > MAX_BODY_BYTES) throw new AppError(413, 'Request is too large');
    chunks.push(chunk);
  }
  if (size === 0) return undefined;
  try {
    return JSON.parse(Buffer.concat(chunks).toString('utf8'));
  } catch {
    throw new AppError(400, 'Request body is not valid JSON');
  }
}

/**
 * The app listens on loopback only, but a web page in the browser could still try to
 * reach it. Reject foreign Host headers (DNS rebinding) and cross-origin requests, and
 * require a JSON content type for bodies so a plain HTML form can't post to us.
 */
function checkRequestOrigin(req, port) {
  const allowedHosts = [`localhost:${port}`, `127.0.0.1:${port}`, `[::1]:${port}`];
  if (!allowedHosts.includes(req.headers.host)) throw new AppError(403, 'Forbidden host');
  const origin = req.headers.origin;
  if (origin && !allowedHosts.some((h) => origin === `http://${h}`)) throw new AppError(403, 'Forbidden origin');
  if (req.method !== 'GET' && req.method !== 'HEAD') {
    const hasBody = Number(req.headers['content-length'] ?? 0) > 0 || req.headers['transfer-encoding'] !== undefined;
    const isJson = (req.headers['content-type'] ?? '').startsWith('application/json');
    if ((hasBody || req.method === 'POST') && !isJson) throw new AppError(415, 'Expected application/json');
  }
}

async function serveStatic(req, res, pathname) {
  if (req.method !== 'GET' && req.method !== 'HEAD') return false;
  for (const { prefix, dir } of STATIC_ROOTS) {
    if (!pathname.startsWith(prefix)) continue;
    let rel = decodeURIComponent(pathname.slice(prefix.length));
    if (rel === '' || rel.endsWith('/')) rel += 'index.html';
    const file = path.resolve(dir, rel);
    if (!file.startsWith(dir + path.sep)) return false;
    try {
      const data = await fs.readFile(file);
      res.writeHead(200, {
        'Content-Type': MIME[path.extname(file)] ?? 'application/octet-stream',
        'Cache-Control': 'no-cache',
        'X-Content-Type-Options': 'nosniff',
      });
      res.end(req.method === 'HEAD' ? undefined : data);
      return true;
    } catch (err) {
      if (err.code === 'ENOENT' || err.code === 'EISDIR') continue;
      throw err;
    }
  }
  return false;
}

// ---------------------------------------------------------------- server

export function createServer({ db, log = console }) {
  const routes = buildRoutes(db);

  const server = http.createServer(async (req, res) => {
    const { pathname } = new URL(req.url, 'http://localhost');
    try {
      checkRequestOrigin(req, server.address().port);

      if (pathname.startsWith('/api/')) {
        let matchedPath = false;
        for (const r of routes) {
          const m = r.re.exec(pathname);
          if (!m) continue;
          matchedPath = true;
          if (r.method !== req.method) continue;
          const params = Object.fromEntries(r.keys.map((k, i) => [k, decodeURIComponent(m[i + 1])]));
          const body = await readJson(req);
          const result = await r.handler({ params, body, req, res });
          if (res.writableEnded) return; // handler streamed its own response
          return result?.[CREATED] ? send(res, 201, result.payload) : send(res, 200, result);
        }
        throw new AppError(matchedPath ? 405 : 404, matchedPath ? 'Method not allowed' : 'Not found');
      }

      if (await serveStatic(req, res, pathname)) return;
      throw new AppError(404, 'Not found');
    } catch (err) {
      if (err instanceof AppError) {
        return send(res, err.status, { error: err.message, ...(err.details && { details: err.details }) });
      }
      log.error(err);
      return send(res, 500, { error: 'Something went wrong on the server — see the terminal for details' });
    }
  });

  return server;
}
