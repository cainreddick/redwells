// At-a-glance totals for one month, compared with the previous saved month.
import { html } from '../html.js';
import { money, percent } from '../format.js';

/**
 * Change vs the previous month. `goodWhen` says which direction is good news
 * ('up' | 'down' | null for neutral); colour is always paired with an arrow and a sign.
 */
function Delta({ now, before, goodWhen, vsLabel }) {
  if (before == null) return null;
  const diff = now - before;
  if (diff === 0) return html`<div class="delta muted">No change vs ${vsLabel}</div>`;
  const up = diff > 0;
  const tone = goodWhen == null ? '' : (up === (goodWhen === 'up') ? 'positive' : 'negative');
  return html`
    <div class="delta ${tone}">
      <span aria-hidden="true">${up ? '▲' : '▼'}</span>
      ${up ? '+' : '−'}${money(Math.abs(diff))} <span class="muted">vs ${vsLabel}</span>
    </div>
  `;
}

function Tile({ label, value, children, className = '' }) {
  return html`
    <div class="tile ${className}">
      <div class="tile-label">${label}</div>
      <div class="tile-value">${value}</div>
      ${children}
    </div>
  `;
}

const SEGMENTS = [
  { key: 'totalBills', label: 'Fixed bills', cls: 'seg-bills' },
  { key: 'totalContributions', label: 'Savings', cls: 'seg-savings' },
  { key: 'totalDebtPayments', label: 'Debt payments', cls: 'seg-debt' },
];

/** Where the income goes: one stacked bar, scaled to income (or to outgoings if overspent). */
function AllocationBar({ s }) {
  const outgoings = s.totalBills + s.totalContributions + s.totalDebtPayments;
  const scale = Math.max(s.totalIncome, outgoings);
  if (scale === 0) return html`<p class="muted small">Add income and outgoings to see where the money goes.</p>`;

  const parts = SEGMENTS.map((seg) => ({ ...seg, value: s[seg.key] })).filter((p) => p.value > 0);
  if (s.leftOver > 0) parts.push({ key: 'leftOver', label: 'Left over', cls: 'seg-left', value: s.leftOver });
  const share = (v) => (s.totalIncome > 0 ? percent(v / s.totalIncome) : '—');
  const overspent = outgoings > s.totalIncome;

  return html`
    <div class="allocation">
      <div class="allocation-bar" role="img" aria-label=${parts.map((p) => `${p.label} ${money(p.value)}`).join(', ')}>
        ${parts.map((p) => html`
          <span
            class="seg ${p.cls}"
            style=${{ flexGrow: p.value }}
            title=${`${p.label}: ${money(p.value)} (${share(p.value)} of income)`}
          ></span>
        `)}
        ${overspent && s.totalIncome > 0 && html`
          <span class="income-marker" style=${{ left: `${(s.totalIncome / scale) * 100}%` }} title=${`Income: ${money(s.totalIncome)}`}></span>
        `}
      </div>
      <ul class="legend">
        ${parts.map((p) => html`
          <li>
            <span class="swatch ${p.cls}" aria-hidden="true"></span>
            ${p.label} <strong>${money(p.value)}</strong>
            <span class="muted">${share(p.value)}</span>
          </li>
        `)}
        ${overspent && html`
          <li class="negative">
            <span class="swatch swatch-marker" aria-hidden="true"></span>
            Income ${money(s.totalIncome)}. Outgoings are ${money(outgoings - s.totalIncome)} over.
          </li>
        `}
      </ul>
    </div>
  `;
}

export function Summary({ summary: s, previous }) {
  const p = previous?.summary;
  const vs = previous?.shortLabel;
  const negative = s.leftOver < 0;

  return html`
    <section class="card summary" aria-label="Monthly summary">
      <div class="summary-flow">
        <${Tile} label="Left over" value=${money(s.leftOver)} className="tile-hero ${negative ? 'is-negative' : ''}">
          ${negative && html`<div class="tile-note"><span aria-hidden="true">⚠</span> Overspent: outgoings are more than income</div>`}
          <${Delta} now=${s.leftOver} before=${p?.leftOver} goodWhen="up" vsLabel=${vs} />
        <//>
        <${Tile} label="Income" value=${money(s.totalIncome)}>
          <${Delta} now=${s.totalIncome} before=${p?.totalIncome} goodWhen="up" vsLabel=${vs} />
        <//>
        <${Tile} label="Fixed bills" value=${money(s.totalBills)}>
          <${Delta} now=${s.totalBills} before=${p?.totalBills} goodWhen="down" vsLabel=${vs} />
        <//>
        <${Tile} label="Savings contributions" value=${money(s.totalContributions)}>
          <${Delta} now=${s.totalContributions} before=${p?.totalContributions} goodWhen="up" vsLabel=${vs} />
        <//>
        <${Tile} label="Debt payments" value=${money(s.totalDebtPayments)}>
          <${Delta} now=${s.totalDebtPayments} before=${p?.totalDebtPayments} goodWhen=${null} vsLabel=${vs} />
        <//>
      </div>

      <${AllocationBar} s=${s} />

      <div class="summary-balances">
        <${Tile} label="Total savings (all pots)" value=${money(s.totalSavings)}>
          <${Delta} now=${s.totalSavings} before=${p?.totalSavings} goodWhen="up" vsLabel=${vs} />
          ${s.totalWithdrawals > 0 && html`<div class="tile-note muted">Includes ${money(s.totalWithdrawals)} withdrawn this month</div>`}
        <//>
        <${Tile} label="Total remaining debt" value=${money(s.totalDebt)}>
          <${Delta} now=${s.totalDebt} before=${p?.totalDebt} goodWhen="down" vsLabel=${vs} />
        <//>
      </div>
    </section>
  `;
}
