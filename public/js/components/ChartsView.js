// Charts across a chosen range of saved months. Chart.js is loaded on first visit.
import { useEffect, useMemo, useRef, useState } from 'preact/hooks';
import { html } from '../html.js';
import { api } from '../api.js';
import { money, shortMonth } from '../format.js';

// ---------------------------------------------------------------- Chart.js loading & theme

let chartJsPromise = null;
function loadChartJs() {
  chartJsPromise ??= new Promise((resolve, reject) => {
    const s = document.createElement('script');
    s.src = '/vendor/chart.umd.min.js';
    s.onload = () => resolve(window.Chart);
    s.onerror = () => { chartJsPromise = null; reject(new Error('Could not load the charting library')); };
    document.head.append(s);
  });
  return chartJsPromise;
}

/** Colours come from CSS custom properties so light/dark stay in one place (app.css). */
function readTheme() {
  const css = getComputedStyle(document.documentElement);
  const v = (name) => css.getPropertyValue(name).trim();
  return {
    text: v('--text'),
    muted: v('--muted'),
    grid: v('--border'),
    surface: v('--surface'),
    income: v('--c-income'),
    bills: v('--seg-bills'),
    savings: v('--seg-savings'),
    debt: v('--seg-debt'),
    series: Array.from({ length: 8 }, (_, i) => v(`--series-${i + 1}`)),
  };
}

/** Re-render charts when the OS switches between light and dark. */
function useColorScheme() {
  const [scheme, setScheme] = useState(() => matchMedia('(prefers-color-scheme: dark)').matches);
  useEffect(() => {
    const mq = matchMedia('(prefers-color-scheme: dark)');
    const on = (e) => setScheme(e.matches);
    mq.addEventListener('change', on);
    return () => mq.removeEventListener('change', on);
  }, []);
  return scheme;
}

const pounds = (pence) => (pence == null ? null : pence / 100);
const axisMoney = new Intl.NumberFormat('en-GB', { style: 'currency', currency: 'GBP', maximumFractionDigits: 0 });

function lineOptions(theme, opts) {
  const o = baseOptions(theme, opts);
  o.scales.x.offset = true; // keep first/last points off the chart edges
  return o;
}

function baseOptions(theme, { legend = true } = {}) {
  return {
    responsive: true,
    maintainAspectRatio: false,
    animation: { duration: 250 },
    interaction: { mode: 'index', intersect: false },
    plugins: {
      legend: {
        display: legend,
        position: 'top',
        align: 'start',
        labels: { color: theme.text, usePointStyle: true, pointStyle: 'rectRounded', boxWidth: 10, boxHeight: 10, padding: 16 },
      },
      tooltip: {
        callbacks: {
          label: (ctx) => (ctx.parsed.y == null ? null : ` ${ctx.dataset.label}: ${money(Math.round(ctx.parsed.y * 100))}`),
        },
      },
    },
    scales: {
      x: { grid: { display: false }, ticks: { color: theme.muted }, border: { color: theme.grid } },
      y: {
        beginAtZero: true,
        grid: { color: theme.grid },
        border: { display: false },
        ticks: { color: theme.muted, callback: (v) => axisMoney.format(v), maxTicksLimit: 6 },
      },
    },
  };
}

function lineDataset(label, data, color, theme) {
  return {
    type: 'line',
    label,
    data,
    borderColor: color,
    backgroundColor: color,
    pointBackgroundColor: color,
    borderWidth: 2,
    pointRadius: 4,
    pointHoverRadius: 6,
    pointBorderColor: theme.surface,
    pointBorderWidth: 2,
    tension: 0,
    spanGaps: false,
  };
}

// ---------------------------------------------------------------- chart definitions

const FLOWS = [
  { key: 'totalIncome', label: 'Income', color: 'income' },
  { key: 'totalBills', label: 'Fixed bills', color: 'bills' },
  { key: 'totalContributions', label: 'Savings contributions', color: 'savings' },
  { key: 'totalDebtPayments', label: 'Debt payments', color: 'debt' },
];

function comparisonConfig(h, theme) {
  return {
    type: 'bar',
    data: {
      labels: h.months.map((m) => shortMonth(m.year, m.month)),
      datasets: FLOWS.map((f) => ({
        label: f.label,
        data: h.months.map((m) => pounds(m.summary[f.key])),
        backgroundColor: theme[f.color],
        borderRadius: 4,
        borderSkipped: 'start',
        categoryPercentage: 0.75,
        barPercentage: 0.9,
      })),
    },
    options: baseOptions(theme),
  };
}

/** Up to 8 pots get their own colour, in first-seen order; any beyond that fold into "Other". */
function potSeries(h) {
  const maxIndex = Math.max(-1, ...h.pots.map((p) => p.index));
  if (maxIndex < 8) return h.pots.map((p) => ({ label: p.name, slot: p.index, balances: p.balances }));
  const own = h.pots.filter((p) => p.index < 7).map((p) => ({ label: p.name, slot: p.index, balances: p.balances }));
  const rest = h.pots.filter((p) => p.index >= 7);
  if (rest.length) {
    own.push({
      label: `Other (${rest.length} pots)`,
      slot: 7,
      balances: h.months.map((_, i) =>
        rest.some((p) => p.balances[i] != null) ? rest.reduce((t, p) => t + (p.balances[i] ?? 0), 0) : null),
    });
  }
  return own;
}

function potsConfig(h, theme) {
  return {
    type: 'line',
    data: {
      labels: h.months.map((m) => shortMonth(m.year, m.month)),
      datasets: potSeries(h).map((s) => lineDataset(s.label, s.balances.map(pounds), theme.series[s.slot], theme)),
    },
    options: lineOptions(theme),
  };
}

function debtConfig(h, theme) {
  const ds = lineDataset('Total debt', h.months.map((m) => pounds(m.summary.totalDebt)), theme.debt, theme);
  ds.fill = 'origin';
  ds.backgroundColor = theme.debt + '26'; // ~15% alpha wash under the line
  return {
    type: 'line',
    data: { labels: h.months.map((m) => shortMonth(m.year, m.month)), datasets: [ds] },
    options: lineOptions(theme, { legend: false }),
  };
}

// ---------------------------------------------------------------- tables (same data, readable without colour)

function DataTable({ columns, rows }) {
  return html`
    <div class="table-scroll">
      <table class="items chart-table">
        <thead><tr>${columns.map((c, i) => html`<th class=${i ? 'num' : ''}>${c}</th>`)}</tr></thead>
        <tbody>
          ${rows.map((r) => html`<tr>${r.map((cell, i) => html`<td class=${i ? 'num' : ''}>${cell}</td>`)}</tr>`)}
        </tbody>
      </table>
    </div>
  `;
}

const cell = (pence) => (pence == null ? html`<span class="muted">—</span>` : money(pence));

function comparisonTable(h) {
  return {
    columns: ['Month', ...FLOWS.map((f) => f.label), 'Left over'],
    rows: h.months.map((m) => [
      m.label,
      ...FLOWS.map((f) => money(m.summary[f.key])),
      html`<span class=${m.summary.leftOver < 0 ? 'negative' : ''}>${money(m.summary.leftOver)}</span>`,
    ]),
  };
}

function potsTable(h) {
  return {
    columns: ['Month', ...h.pots.map((p) => p.name), 'Total'],
    rows: h.months.map((m, i) => [m.label, ...h.pots.map((p) => cell(p.balances[i])), money(m.summary.totalSavings)]),
  };
}

function debtTable(h) {
  return {
    columns: ['Month', ...h.debts.map((d) => d.name), 'Total'],
    rows: h.months.map((m, i) => [m.label, ...h.debts.map((d) => cell(d.balances[i])), money(m.summary.totalDebt)]),
  };
}

// ---------------------------------------------------------------- chart card

function ChartCard({ title, subtitle, Chart, config, table, empty }) {
  const canvas = useRef();
  const [showTable, setShowTable] = useState(false);

  useEffect(() => {
    if (!Chart || !config || showTable) return;
    const chart = new Chart(canvas.current, config);
    return () => chart.destroy();
  }, [Chart, config, showTable]);

  return html`
    <section class="card chart-card">
      <header class="card-head">
        <div>
          <h2>${title}</h2>
          ${subtitle && html`<p class="muted small chart-subtitle">${subtitle}</p>`}
        </div>
        ${!empty && html`
          <button type="button" class="btn btn-sm btn-ghost" aria-pressed=${showTable} onClick=${() => setShowTable((v) => !v)}>
            ${showTable ? 'Show chart' : 'Show table'}
          </button>
        `}
      </header>
      ${empty
        ? html`<p class="empty">${empty}</p>`
        : showTable
          ? html`<${DataTable} ...${table} />`
          : html`<div class="chart-box"><canvas ref=${canvas} role="img" aria-label=${title}></canvas></div>`}
    </section>
  `;
}

// ---------------------------------------------------------------- range picker

const RANGE_KEY = 'budget.chartRange';
const PRESETS = [
  { id: '6', label: 'Last 6' },
  { id: '12', label: 'Last 12' },
  { id: 'all', label: 'All' },
];

function rangeForPreset(available, id) {
  const keys = available.map((a) => a.key);
  const n = id === 'all' ? keys.length : Number(id);
  return { from: keys[Math.max(0, keys.length - n)], to: keys.at(-1), preset: id };
}

function loadSavedRange(available) {
  try {
    const saved = JSON.parse(localStorage.getItem(RANGE_KEY));
    if (saved?.preset) return rangeForPreset(available, saved.preset);
    const keys = new Set(available.map((a) => a.key));
    if (keys.has(saved?.from) && keys.has(saved?.to)) return { from: saved.from, to: saved.to, preset: null };
  } catch { /* storage unavailable or corrupt: fall through to the default */ }
  return rangeForPreset(available, '12');
}

function saveRange(range) {
  try { localStorage.setItem(RANGE_KEY, JSON.stringify(range)); } catch { /* per-browser convenience only */ }
}

function RangePicker({ available, range, onChange }) {
  const set = (patch) => {
    const next = { ...range, ...patch, preset: null };
    if (next.from > next.to) [next.from, next.to] = [next.to, next.from];
    onChange(next);
  };
  return html`
    <div class="range-picker" role="group" aria-label="Months to compare">
      <label class="range-field">
        <span class="field-label">From</span>
        <select value=${range.from} onChange=${(e) => set({ from: e.currentTarget.value })}>
          ${available.map((a) => html`<option value=${a.key}>${a.label}</option>`)}
        </select>
      </label>
      <label class="range-field">
        <span class="field-label">To</span>
        <select value=${range.to} onChange=${(e) => set({ to: e.currentTarget.value })}>
          ${available.map((a) => html`<option value=${a.key}>${a.label}</option>`)}
        </select>
      </label>
      <div class="segmented" role="group" aria-label="Quick ranges">
        ${PRESETS.map((p) => html`
          <button
            type="button"
            class="btn btn-sm ${range.preset === p.id ? 'is-active' : ''}"
            aria-pressed=${range.preset === p.id}
            onClick=${() => onChange(rangeForPreset(available, p.id))}
          >${p.label}</button>
        `)}
      </div>
    </div>
  `;
}

// ---------------------------------------------------------------- page

export function ChartsView() {
  const [available, setAvailable] = useState(null);
  const [range, setRange] = useState(null);
  const [history, setHistory] = useState(null);
  const [Chart, setChart] = useState(null);
  const [error, setError] = useState(null);
  const dark = useColorScheme();

  useEffect(() => {
    loadChartJs().then((C) => {
      C.defaults.font.family = getComputedStyle(document.body).fontFamily;
      C.defaults.font.size = 12;
      setChart(() => C);
    }, (e) => setError(e.message));
    api.history().then((h) => {
      setAvailable(h.available);
      if (h.available.length) setRange(loadSavedRange(h.available));
    }, (e) => setError(e.message));
  }, []);

  useEffect(() => {
    if (!range) return;
    saveRange(range);
    let live = true;
    api.history({ from: range.from, to: range.to }).then((h) => live && setHistory(h), (e) => live && setError(e.message));
    return () => { live = false; };
  }, [range?.from, range?.to]);

  // Build configs once per data/theme change so charts aren't torn down on unrelated renders.
  const configs = useMemo(() => {
    if (!history || !Chart) return null;
    const theme = readTheme();
    return {
      comparison: comparisonConfig(history, theme),
      pots: potsConfig(history, theme),
      debt: debtConfig(history, theme),
    };
  }, [history, Chart, dark]);

  if (error) return html`<div class="card empty-state"><p class="negative">${error}</p></div>`;
  if (!available) return html`<div class="loading">Loading…</div>`;
  if (!available.length) {
    return html`<div class="card empty-state"><h1>Charts</h1><p>Create a month first. Charts appear once you have data.</p></div>`;
  }

  const h = history;
  const n = h?.months.length ?? 0;
  const span = h && n ? `${h.months[0].label} – ${h.months.at(-1).label} · ${n} saved month${n === 1 ? '' : 's'}` : '';

  return html`
    <article class="charts-view">
      <header class="month-head">
        <div>
          <h1>Charts</h1>
          <p class="muted small">${span}</p>
        </div>
      </header>

      <div class="card range-card">
        <${RangePicker} available=${available} range=${range} onChange=${setRange} />
      </div>

      ${!h || !configs
        ? html`<div class="loading">Loading…</div>`
        : html`
          <div class="charts-grid">
            <${ChartCard}
              title="Month-to-month comparison"
              subtitle="Income against where it went"
              Chart=${Chart}
              config=${configs.comparison}
              table=${comparisonTable(h)}
            />
            <${ChartCard}
              title="Savings pot balances"
              subtitle="Closing balance of each pot at the end of each month"
              Chart=${Chart}
              config=${configs.pots}
              table=${potsTable(h)}
              empty=${h.pots.length ? null : 'No savings pots in these months.'}
            />
            <${ChartCard}
              title="Total debt balance"
              subtitle="All debts combined, after interest and payments"
              Chart=${Chart}
              config=${configs.debt}
              table=${debtTable(h)}
              empty=${h.debts.length ? null : 'No debts in these months.'}
            />
          </div>
        `}
    </article>
  `;
}
