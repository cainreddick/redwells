// Full backup download and restore, plus where the data lives on disk.
import { useEffect, useRef, useState } from 'preact/hooks';
import { html } from '../html.js';
import { api } from '../api.js';
import { confirmAction, toast } from '../ui.js';
import { dateTime, plural } from '../format.js';
import { monthLabel } from '/shared/calc.js';

const size = (bytes) => (bytes < 1024 * 1024 ? `${Math.max(1, Math.round(bytes / 1024))} KB` : `${(bytes / 1024 / 1024).toFixed(1)} MB`);

export function BackupView({ onRestored }) {
  const [info, setInfo] = useState(null);
  const [busy, setBusy] = useState(false);
  const fileInput = useRef();

  const loadInfo = () => api.info().then(setInfo, (e) => toast(e.message, 'error'));
  useEffect(() => { loadInfo(); }, []);

  async function restoreFrom(file) {
    let data;
    try {
      data = JSON.parse(await file.text());
    } catch {
      return toast(`${file.name} isn't a valid backup file (it isn't JSON).`, 'error');
    }
    const months = Array.isArray(data?.months) ? data.months : [];
    const sorted = months.filter((m) => m?.year && m?.month).sort((a, b) => a.year - b.year || a.month - b.month);
    const range = sorted.length
      ? `${monthLabel(sorted[0].year, sorted[0].month)} – ${monthLabel(sorted.at(-1).year, sorted.at(-1).month)}`
      : 'no months';

    const ok = await confirmAction({
      title: 'Replace all data with this backup?',
      message: html`
        <p><strong>${file.name}</strong>${data?.exportedAt ? html`, made ${dateTime(data.exportedAt)}` : ''}</p>
        <p>It contains ${plural(months.length, 'month')} (${range}).</p>
        <p>Every month currently in the app will be replaced. A snapshot of your current data is saved to the backups folder first, so this can be undone.</p>
      `,
      confirmLabel: 'Restore backup',
    });
    if (!ok) return;

    setBusy(true);
    try {
      const r = await api.restore(data);
      toast(
        html`<strong>Restored ${plural(r.months, 'month')}.</strong>${r.snapshot && html`<br />Your previous data was saved as ${r.snapshot}.`}`,
        'success',
        10000,
      );
      onRestored();
      loadInfo();
    } catch (e) {
      toast(e.message, 'error', 12000);
    } finally {
      setBusy(false);
    }
  }

  return html`
    <article class="backup-view">
      <header class="month-head">
        <div>
          <h1>Backup & restore</h1>
          <p class="muted small">Everything is stored on this computer in a single file.</p>
        </div>
      </header>

      <div class="backup-grid">
        <section class="card">
          <h2>Download a backup</h2>
          <p class="muted">
            Saves every month, with all of its items and notes, as one JSON file. Keep it
            somewhere safe, like a USB stick or cloud storage.
          </p>
          <a class="btn btn-primary" href="/api/backup" download>Download backup</a>
        </section>

        <section class="card">
          <h2>Restore from a backup</h2>
          <p class="muted">
            Replaces <strong>all</strong> current data with the contents of a backup file.
            The file is fully checked first, and nothing changes if it has a problem.
          </p>
          <input
            ref=${fileInput}
            type="file"
            accept=".json,application/json"
            class="visually-hidden"
            onChange=${(e) => {
              const f = e.currentTarget.files[0];
              e.currentTarget.value = ''; // allow picking the same file again
              if (f) restoreFrom(f);
            }}
          />
          <button type="button" class="btn" disabled=${busy} onClick=${() => fileInput.current.click()}>
            ${busy ? 'Restoring…' : 'Choose backup file…'}
          </button>
        </section>
      </div>

      <section class="card storage-card">
        <h2>Where your data lives</h2>
        ${!info
          ? html`<p class="muted">Loading…</p>`
          : html`
            <dl class="storage">
              <dt>Data file</dt>
              <dd><code>${info.dataFile ?? '(in memory)'}</code></dd>
              <dt>Snapshots</dt>
              <dd><code>${info.backupDir ?? '—'}</code></dd>
            </dl>
            <p class="muted small">
              Copying the data file while the app is stopped is also a complete backup. A
              snapshot is saved automatically each time the app starts, and before every
              restore. The newest 10 of each are kept. To go back to a snapshot, stop the app
              and copy it over the data file (see README).
            </p>
            ${info.snapshots.length > 0 && html`
              <table class="items snapshots">
                <thead><tr><th>Snapshot</th><th>Saved</th><th class="num">Size</th></tr></thead>
                <tbody>
                  ${info.snapshots.map((s) => html`
                    <tr key=${s.name}><td><code>${s.name}</code></td><td>${dateTime(s.modified)}</td><td class="num">${size(s.size)}</td></tr>
                  `)}
                </tbody>
              </table>
            `}
          `}
      </section>
    </article>
  `;
}
