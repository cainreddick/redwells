// The four tracked sections of a month. Stage 2: read-only tables.
import { html } from '../html.js';
import { money, percent, shortMonth, plural } from '../format.js';
import { dueDateLabel } from '/shared/calc.js';

export function Progress({ fraction }) {
  if (fraction == null) return html`<span class="muted">No target</span>`;
  const pct = Math.max(0, Math.min(1, fraction));
  return html`
    <div class="progress-wrap" title=${percent(fraction)}>
      <div class="progress" role="progressbar" aria-valuemin="0" aria-valuemax="100" aria-valuenow=${Math.round(pct * 100)}>
        <span class=${fraction >= 1 ? 'complete' : ''} style=${{ width: `${pct * 100}%` }}></span>
      </div>
      <span class="progress-label">${percent(fraction)}</span>
    </div>
  `;
}

export function Payoff({ payoff }) {
  if (payoff.status === 'clear') return html`<span class="positive">Paid off</span>`;
  if (payoff.status === 'never') return html`<span class="negative" title="The payment doesn't cover the monthly interest">Never at this payment</span>`;
  return html`
    <span title=${plural(payoff.months, 'payment')}>${shortMonth(payoff.year, payoff.month)}</span>
    <span class="muted small"> (${plural(payoff.months, 'month')})</span>
  `;
}

const num = 'num';

export const SECTIONS = {
  income: {
    title: 'Income',
    empty: 'No income sources yet.',
    columns: [
      { label: 'Source', render: (i) => i.name },
      { label: 'Recurring', render: (i) => (i.recurring ? 'Monthly' : html`<span class="muted">One-off</span>`) },
      { label: 'Amount', cls: num, render: (i) => money(i.amount), total: (s) => money(s.totalIncome) },
    ],
  },
  bills: {
    title: 'Fixed bills',
    empty: 'No bills yet.',
    columns: [
      { label: 'Bill', render: (b) => b.name },
      { label: 'Category', render: (b) => b.category ?? html`<span class="muted">—</span>` },
      { label: 'Due', render: (b, m) => dueDateLabel(m.year, m.month, b.dueDay) || html`<span class="muted">—</span>` },
      { label: 'Amount', cls: num, render: (b) => money(b.amount), total: (s) => money(s.totalBills) },
    ],
  },
  pots: {
    title: 'Savings pots',
    empty: 'No savings pots yet.',
    columns: [
      { label: 'Pot', render: (p) => p.name },
      { label: 'Opening', cls: num, render: (p) => money(p.opening) },
      { label: 'Contribution', cls: num, render: (p) => money(p.contribution), total: (s) => money(s.totalContributions) },
      { label: 'Withdrawn', cls: num, render: (p) => money(p.withdrawal), total: (s) => money(s.totalWithdrawals) },
      { label: 'Balance', cls: num, render: (p) => html`<strong>${money(p.closing)}</strong>`, total: (s) => money(s.totalSavings) },
      { label: 'Target', cls: num, render: (p) => (p.target ? money(p.target) : html`<span class="muted">—</span>`) },
      { label: 'Progress', cls: 'col-progress', render: (p) => html`<${Progress} fraction=${p.progress} />` },
    ],
  },
  debts: {
    title: 'Debts',
    empty: 'No debts. Nice.',
    columns: [
      { label: 'Debt', render: (d) => d.name },
      { label: 'Opening', cls: num, render: (d) => money(d.opening) },
      {
        label: 'Interest',
        cls: num,
        render: (d) => (d.apr ? html`${money(d.interest)} <span class="muted small">@ ${d.apr}%</span>` : html`<span class="muted">—</span>`),
      },
      { label: 'Payment', cls: num, render: (d) => money(d.payment), total: (s) => money(s.totalDebtPayments) },
      { label: 'Closing', cls: num, render: (d) => html`<strong>${money(d.closing)}</strong>`, total: (s) => money(s.totalDebt) },
      { label: 'Paid off by', render: (d) => html`<${Payoff} payoff=${d.payoff} />` },
    ],
  },
};

export function SectionTable({ name, data }) {
  const sec = SECTIONS[name];
  const items = data[name];
  const hasTotals = items.length > 0 && sec.columns.some((c) => c.total);

  return html`
    <section class="card section section-${name}">
      <header class="card-head">
        <h2>${sec.title}</h2>
        <span class="muted small">${plural(items.length, 'item')}</span>
      </header>
      ${items.length === 0
        ? html`<p class="empty">${sec.empty}</p>`
        : html`
          <table class="items">
            <thead>
              <tr>${sec.columns.map((c) => html`<th class=${c.cls}>${c.label}</th>`)}</tr>
            </thead>
            <tbody>
              ${items.map((it) => html`
                <tr key=${it.id}>${sec.columns.map((c) => html`<td class=${c.cls}>${c.render(it, data.month)}</td>`)}</tr>
              `)}
            </tbody>
            ${hasTotals && html`
              <tfoot>
                <tr>
                  ${sec.columns.map((c, i) => html`
                    <td class=${c.cls}>${i === 0 ? 'Total' : c.total ? c.total(data.summary) : ''}</td>
                  `)}
                </tr>
              </tfoot>
            `}
          </table>
        `}
    </section>
  `;
}
