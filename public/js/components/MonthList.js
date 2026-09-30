import { html } from '../html.js';
import { money } from '../format.js';
import { MONTH_NAMES, monthKey } from '/shared/calc.js';

/** Sidebar list of saved months, grouped by year, newest first. */
export function MonthList({ months, selectedKey, onNew }) {
  const years = [];
  for (const m of months) {
    if (years.at(-1)?.year !== m.year) years.push({ year: m.year, months: [] });
    years.at(-1).months.push(m);
  }

  return html`
    <nav class="month-list" aria-label="Saved months">
      <button type="button" class="btn btn-primary btn-block" onClick=${onNew}>+ New month</button>
      ${months.length === 0 && html`<p class="muted small">No months saved yet.</p>`}
      ${years.map(({ year, months }) => html`
        <section key=${year}>
          <h3 class="month-list-year">${year}</h3>
          <ul>
            ${months.map((m) => {
              const key = monthKey(m.year, m.month);
              const left = m.summary.leftOver;
              return html`
                <li key=${key}>
                  <a href="#/${key}" class=${key === selectedKey ? 'active' : ''} aria-current=${key === selectedKey ? 'page' : undefined}>
                    <span>${MONTH_NAMES[m.month - 1]}</span>
                    <span class="month-list-left ${left < 0 ? 'negative' : ''}" title="Left over">${money(left)}</span>
                  </a>
                </li>
              `;
            })}
          </ul>
        </section>
      `)}
    </nav>
  `;
}
