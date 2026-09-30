import { render } from 'preact';
import { useCallback, useEffect, useState } from 'preact/hooks';
import { html } from './html.js';
import { api } from './api.js';
import { ConfirmHost, ToastHost, toast } from './ui.js';
import { MonthList } from './components/MonthList.js';
import { MonthView } from './components/MonthView.js';
import { NewMonthDialog } from './components/NewMonthDialog.js';
import { ChartsView } from './components/ChartsView.js';
import { BackupView } from './components/BackupView.js';
import { shortMonth } from './format.js';
import { monthKey, monthLabel, parseMonthKey } from '/shared/calc.js';

// Routes look like #/2026-10 so a refresh or the back button keeps your place.
function readHash() {
  return location.hash.replace(/^#\/?/, '') || null;
}

function useHashRoute() {
  const [route, setRoute] = useState(readHash);
  useEffect(() => {
    const onChange = () => setRoute(readHash());
    addEventListener('hashchange', onChange);
    return () => removeEventListener('hashchange', onChange);
  }, []);
  const navigate = useCallback((key, { replace = false } = {}) => {
    const url = key ? `#/${key}` : location.pathname;
    if (replace) history.replaceState(null, '', url);
    else history.pushState(null, '', url);
    setRoute(key);
  }, []);
  return [route, navigate];
}

function App() {
  const [months, setMonths] = useState(null);
  const [loadError, setLoadError] = useState(null);
  const [route, navigate] = useHashRoute();
  const [creating, setCreating] = useState(false);

  const refreshMonths = useCallback(
    () => api.listMonths().then(setMonths, (e) => (months ? toast(e.message, 'error') : setLoadError(e.message))),
    [months],
  );

  useEffect(() => { refreshMonths(); }, []);

  // With no month in the URL, open the newest one.
  useEffect(() => {
    if (months?.length && !route) navigate(monthKey(months[0].year, months[0].month), { replace: true });
  }, [months, route]);

  if (loadError) {
    return html`<div class="fatal card"><h1>Couldn't load your budget</h1><p>${loadError}</p></div>`;
  }
  if (!months) return html`<div class="loading">Loading…</div>`;

  const selected = months.find((m) => monthKey(m.year, m.month) === route);
  const routeMonth = parseMonthKey(route);

  // Months are newest first, so the one after the selected month is the previous saved one.
  const prev = selected && months[months.indexOf(selected) + 1];

  let main;
  if (route === 'charts') {
    main = html`<${ChartsView} />`;
  } else if (route === 'backup') {
    main = html`<${BackupView} onRestored=${refreshMonths} />`;
  } else if (selected) {
    main = html`
      <${MonthView}
        key=${selected.id}
        monthId=${selected.id}
        previous=${prev ? { shortLabel: shortMonth(prev.year, prev.month), summary: prev.summary } : null}
        onChanged=${refreshMonths}
        onDeleted=${() => {
          const rest = months.filter((m) => m.id !== selected.id);
          setMonths(rest);
          navigate(rest[0] ? monthKey(rest[0].year, rest[0].month) : null, { replace: true });
          refreshMonths();
        }}
      />`;
  } else if (months.length === 0) {
    main = html`
      <div class="card empty-state">
        <h1>Welcome</h1>
        <p>Each month is saved as its own record. Create your first one to get started.</p>
        <button type="button" class="btn btn-primary" onClick=${() => setCreating(true)}>Create your first month</button>
      </div>`;
  } else {
    main = html`
      <div class="card empty-state">
        <h1>${routeMonth ? `${monthLabel(routeMonth.year, routeMonth.month)} isn't saved` : 'Month not found'}</h1>
        <p>Pick a month from the list, or create a new one.</p>
      </div>`;
  }

  return html`
    <div class="layout">
      <aside class="sidebar">
        <div class="brand"><span class="brand-mark">£</span> Household Budget</div>
        <a href="#/charts" class="nav-link ${route === 'charts' ? 'active' : ''}" aria-current=${route === 'charts' ? 'page' : undefined}>
          <svg viewBox="0 0 20 20" width="16" height="16" aria-hidden="true"><path fill="currentColor" d="M3 16.5h14V18H3zM4 10h2.5v5H4zm4.75-4h2.5v9h-2.5zM13.5 8H16v7h-2.5z"/></svg>
          Charts
        </a>
        <a href="#/backup" class="nav-link ${route === 'backup' ? 'active' : ''}" aria-current=${route === 'backup' ? 'page' : undefined}>
          <svg viewBox="0 0 20 20" width="16" height="16" aria-hidden="true"><path fill="currentColor" d="M10 2c-4 0-7 1.3-7 3v10c0 1.7 3 3 7 3s7-1.3 7-3V5c0-1.7-3-3-7-3zm0 1.5c3.4 0 5.5 1 5.5 1.5S13.4 6.5 10 6.5 4.5 5.5 4.5 5 6.6 3.5 10 3.5zm5.5 11.5c0 .5-2.1 1.5-5.5 1.5S4.5 15.5 4.5 15v-2.2c1.3.7 3.3 1.2 5.5 1.2s4.2-.5 5.5-1.2V15zm0-4.5c0 .5-2.1 1.5-5.5 1.5s-5.5-1-5.5-1.5V8.3c1.3.7 3.3 1.2 5.5 1.2s4.2-.5 5.5-1.2v2.2z"/></svg>
          Backup & restore
        </a>
        <${MonthList} months=${months} selectedKey=${route} onNew=${() => setCreating(true)} />
      </aside>
      <main class="content">${main}</main>
    </div>
    <${NewMonthDialog}
      open=${creating}
      months=${months}
      onClose=${() => setCreating(false)}
      onCreated=${(created) => {
        setCreating(false);
        refreshMonths().then(() => navigate(monthKey(created.month.year, created.month.month)));
      }}
    />
    <${ConfirmHost} />
    <${ToastHost} />
  `;
}

render(html`<${App} />`, document.getElementById('app'));
