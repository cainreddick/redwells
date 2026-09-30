// The four tracked sections of a month, as editable tables.
//
// Each section is described by a list of columns. A column with a `field` becomes an input
// when its row is being edited; other columns are computed and show a live preview of the
// draft while editing (e.g. a pot's balance updates as you type the contribution).

import { useLayoutEffect, useRef, useState } from 'preact/hooks';
import { html } from '../html.js';
import { api } from '../api.js';
import { confirmAction, toast } from '../ui.js';
import { money, percent, shortMonth, plural } from '../format.js';
import { debtFigures, dueDateLabel, penceToInput, potFigures } from '/shared/calc.js';
import { BILL_CATEGORIES, validateItem } from '/shared/validate.js';

// ---------------------------------------------------------------- small displays

const dash = html`<span class="muted">—</span>`;

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
  if (payoff.status === 'never') {
    return html`<span class="negative" title="The payment doesn't cover the monthly interest, so the balance never falls">Never</span>`;
  }
  return html`
    <span title=${plural(payoff.months, 'payment')}>${shortMonth(payoff.year, payoff.month)}</span>
    <span class="muted small"> (${plural(payoff.months, 'month')})</span>
  `;
}

// ---------------------------------------------------------------- section definitions

const num = 'num';
const money$ = (key) => (it) => money(it[key]);

export const SECTIONS = {
  income: {
    title: 'Income',
    noun: 'income source',
    empty: 'No income sources yet.',
    blank: { name: '', amount: '', recurring: true },
    columns: [
      { label: 'Source', render: (i) => i.name, field: { key: 'name', type: 'text', placeholder: 'e.g. Salary' } },
      {
        label: 'Recurring',
        render: (i) => (i.recurring ? 'Monthly' : html`<span class="muted">One-off</span>`),
        field: { key: 'recurring', type: 'bool', label: 'Monthly' },
      },
      { label: 'Amount', cls: num, render: money$('amount'), field: { key: 'amount', type: 'money' }, total: (s) => s.totalIncome },
    ],
  },
  bills: {
    title: 'Fixed bills',
    noun: 'bill',
    empty: 'No bills yet.',
    blank: { name: '', amount: '', category: '', dueDay: '' },
    columns: [
      { label: 'Bill', render: (b) => b.name, field: { key: 'name', type: 'text', placeholder: 'e.g. Mortgage' } },
      { label: 'Category', render: (b) => b.category ?? dash, field: { key: 'category', type: 'select', options: BILL_CATEGORIES } },
      {
        label: 'Due',
        render: (b, m) => dueDateLabel(m.year, m.month, b.dueDay) || dash,
        field: { key: 'dueDay', type: 'day', title: 'Day of the month (1–31). Leave blank if it varies' },
      },
      { label: 'Amount', cls: num, render: money$('amount'), field: { key: 'amount', type: 'money' }, total: (s) => s.totalBills },
    ],
  },
  pots: {
    title: 'Savings pots',
    noun: 'savings pot',
    empty: 'No savings pots yet.',
    blank: { name: '', target: '', opening: '', contribution: '', withdrawal: '' },
    columns: [
      { label: 'Pot', render: (p) => p.name, field: { key: 'name', type: 'text', placeholder: 'e.g. Holiday' } },
      { label: 'Opening', cls: num, render: money$('opening'), field: { key: 'opening', type: 'money' } },
      {
        label: 'Contribution', cls: num, render: money$('contribution'),
        field: { key: 'contribution', type: 'money' }, total: (s) => s.totalContributions,
      },
      {
        label: 'Withdrawn', cls: num, render: money$('withdrawal'),
        field: { key: 'withdrawal', type: 'money' }, total: (s) => s.totalWithdrawals,
      },
      { label: 'Balance', cls: num, render: (p) => html`<strong>${money(p.closing)}</strong>`, total: (s) => s.totalSavings },
      {
        label: 'Target', cls: num, render: (p) => (p.target ? money(p.target) : dash),
        field: { key: 'target', type: 'money', placeholder: 'Optional' },
      },
      { label: 'Progress', cls: 'col-progress', render: (p) => html`<${Progress} fraction=${p.progress} />` },
    ],
    preview: (v) => ({ ...v, ...potFigures(v) }),
  },
  debts: {
    title: 'Debts',
    noun: 'debt',
    empty: 'No debts. Nice.',
    blank: { name: '', opening: '', payment: '', apr: '' },
    columns: [
      { label: 'Debt', render: (d) => d.name, field: { key: 'name', type: 'text', placeholder: 'e.g. Credit card' } },
      { label: 'Opening', cls: num, render: money$('opening'), field: { key: 'opening', type: 'money' } },
      {
        label: 'APR', cls: num, render: (d) => (d.apr ? `${d.apr}%` : dash),
        field: { key: 'apr', type: 'percent', placeholder: 'Optional' },
      },
      { label: 'Interest', cls: num, render: (d) => (d.interest ? money(d.interest) : dash) },
      { label: 'Payment', cls: num, render: money$('payment'), field: { key: 'payment', type: 'money' }, total: (s) => s.totalDebtPayments },
      { label: 'Closing', cls: num, render: (d) => html`<strong>${money(d.closing)}</strong>`, total: (s) => s.totalDebt },
      { label: 'Paid off by', cls: 'nowrap', render: (d) => html`<${Payoff} payoff=${d.payoff} />` },
    ],
    preview: (v, m) => ({ ...v, ...debtFigures(v, m.year, m.month) }),
  },
};

// ---------------------------------------------------------------- drafts

/** Item (pence, booleans, nulls) → draft (strings for inputs). */
function toDraft(sectionName, item) {
  const draft = {};
  for (const { field } of SECTIONS[sectionName].columns) {
    if (!field) continue;
    const v = item[field.key];
    draft[field.key] =
      field.type === 'bool' ? !!v
      : field.type === 'money' ? penceToInput(v)
      : v == null ? '' : String(v);
  }
  return draft;
}

// ---------------------------------------------------------------- inputs

function FieldInput({ field, value, error, onChange, inputRef, label }) {
  const common = {
    ref: inputRef,
    'aria-invalid': error ? 'true' : undefined,
    'aria-label': label,
    title: error || field.title,
  };
  const set = (e) => onChange(e.currentTarget.value);

  let input;
  switch (field.type) {
    case 'bool':
      input = html`
        <label class="inline-check">
          <input type="checkbox" ...${common} checked=${value} onChange=${(e) => onChange(e.currentTarget.checked)} />
          ${field.label}
        </label>`;
      break;
    case 'select':
      input = html`
        <select ...${common} value=${value} onChange=${set}>
          <option value="">—</option>
          ${field.options.map((o) => html`<option value=${o}>${o}</option>`)}
        </select>`;
      break;
    case 'money':
      input = html`
        <span class="affix affix-pre">
          <span aria-hidden="true">£</span>
          <input type="text" inputmode="decimal" autocomplete="off" class="input-money" placeholder=${field.placeholder ?? '0.00'} ...${common} value=${value} onInput=${set} />
        </span>`;
      break;
    case 'percent':
      input = html`
        <span class="affix affix-post">
          <input type="text" inputmode="decimal" autocomplete="off" class="input-percent" placeholder=${field.placeholder} ...${common} value=${value} onInput=${set} />
          <span aria-hidden="true">%</span>
        </span>`;
      break;
    case 'day':
      input = html`
        <input type="number" min="1" max="31" step="1" class="input-day" placeholder="Day" ...${common} value=${value} onInput=${set} />`;
      break;
    default:
      input = html`
        <input type="text" maxlength="80" autocomplete="off" class="input-text" placeholder=${field.placeholder} ...${common} value=${value} onInput=${set} />`;
  }

  return html`${input}${error && html`<div class="cell-error">${error}</div>`}`;
}

// ---------------------------------------------------------------- rows

function EditRow({ sectionName, month, initial, onSave, onCancel }) {
  const sec = SECTIONS[sectionName];
  const [draft, setDraft] = useState(initial);
  const [errors, setErrors] = useState({});
  const [touched, setTouched] = useState(false);
  const [busy, setBusy] = useState(false);
  const firstInput = useRef();

  useLayoutEffect(() => { firstInput.current?.focus(); }, []);

  const check = validateItem(sectionName, draft);
  const preview = check.ok ? (sec.preview?.(check.value, month) ?? check.value) : null;
  // Once the user has tried to save, keep errors live as they type.
  const shownErrors = touched ? (check.ok ? errors.server ?? {} : check.errors) : {};

  async function save() {
    setTouched(true);
    setErrors({});
    if (!check.ok || busy) return;
    setBusy(true);
    try {
      await onSave(check.value);
    } catch (e) {
      setBusy(false);
      if (e.details) setErrors({ server: e.details });
      else toast(e.message, 'error');
    }
  }

  function onKeyDown(e) {
    if (e.key === 'Enter' && e.target.tagName !== 'BUTTON') { e.preventDefault(); save(); }
    if (e.key === 'Escape') { e.preventDefault(); onCancel(); }
  }

  let firstField = true;
  return html`
    <tr class="editing" onKeyDown=${onKeyDown}>
      ${sec.columns.map((c) => {
        if (!c.field) {
          return html`<td class="${c.cls ?? ''} preview">${preview ? c.render(preview, month) : dash}</td>`;
        }
        const ref = firstField ? firstInput : undefined;
        firstField = false;
        return html`
          <td class=${c.cls}>
            <${FieldInput}
              field=${c.field}
              label=${c.label}
              value=${draft[c.field.key]}
              error=${shownErrors[c.field.key]}
              inputRef=${ref}
              onChange=${(v) => setDraft((d) => ({ ...d, [c.field.key]: v }))}
            />
          </td>`;
      })}
      <td class="row-actions">
        <button type="button" class="btn btn-sm btn-primary" disabled=${busy} onClick=${save}>Save</button>
        <button type="button" class="btn btn-sm" disabled=${busy} onClick=${onCancel}>Cancel</button>
      </td>
    </tr>
  `;
}

const PencilIcon = html`<svg viewBox="0 0 20 20" width="16" height="16" aria-hidden="true"><path fill="currentColor" d="M14.7 2.3a1 1 0 0 1 1.4 0l1.6 1.6a1 1 0 0 1 0 1.4l-9.9 9.9-3.6 1 1-3.6 9.5-10.3zM3 18h14v1.5H3z"/></svg>`;
const TrashIcon = html`<svg viewBox="0 0 20 20" width="16" height="16" aria-hidden="true"><path fill="currentColor" d="M7.5 2h5l.6 1.5H17V5H3V3.5h3.9L7.5 2zM4.5 6.5h11l-.8 11a1.5 1.5 0 0 1-1.5 1.4H6.8a1.5 1.5 0 0 1-1.5-1.4l-.8-11zm3.2 2v8h1.5v-8H7.7zm3.1 0v8h1.5v-8h-1.5z"/></svg>`;

function ViewRow({ sectionName, item, month, locked, onEdit, onDelete }) {
  const sec = SECTIONS[sectionName];
  return html`
    <tr onDblClick=${locked ? undefined : onEdit}>
      ${sec.columns.map((c) => html`<td class=${c.cls}>${c.render(item, month)}</td>`)}
      <td class="row-actions">
        <button type="button" class="icon-btn" title="Edit" aria-label=${`Edit ${item.name}`} disabled=${locked} onClick=${onEdit}>${PencilIcon}</button>
        <button type="button" class="icon-btn icon-danger" title="Delete" aria-label=${`Delete ${item.name}`} disabled=${locked} onClick=${onDelete}>${TrashIcon}</button>
      </td>
    </tr>
  `;
}

// ---------------------------------------------------------------- section

/**
 * One editable section. `onMonth` receives the refreshed month after every change.
 * Only one row per section is edited at a time, so unsaved edits are never silently lost.
 */
export function Section({ name, data, onMonth }) {
  const sec = SECTIONS[name];
  const items = data[name];
  const month = data.month;
  const [editing, setEditing] = useState(null); // null | 'new' | item id

  const hasTotals = items.length > 0 && sec.columns.some((c) => c.total);

  async function add(value) {
    onMonth(await api.addItem(month.id, name, value));
    toast(`Added ${value.name}`, 'success', 2000);
    setEditing(null);
  }

  async function update(id, value) {
    onMonth(await api.updateItem(name, id, value));
    setEditing(null);
  }

  async function remove(item) {
    const ok = await confirmAction({
      title: `Delete ${item.name}?`,
      message: `This removes the ${sec.noun} "${item.name}" from ${month.label}. Other months aren't affected.`,
    });
    if (!ok) return;
    try {
      onMonth(await api.deleteItem(name, item.id));
      toast(`Deleted ${item.name}`, 'success', 2000);
    } catch (e) {
      toast(e.message, 'error');
    }
  }

  return html`
    <section class="card section section-${name}">
      <header class="card-head">
        <h2>${sec.title}</h2>
        <span class="muted small">${plural(items.length, 'item')}</span>
      </header>

      ${items.length === 0 && editing !== 'new'
        ? html`<p class="empty">${sec.empty}</p>`
        : html`
          <div class="table-scroll">
            <table class="items">
              <thead>
                <tr>
                  ${sec.columns.map((c) => html`<th class=${c.cls}>${c.label}</th>`)}
                  <th class="row-actions"><span class="visually-hidden">Actions</span></th>
                </tr>
              </thead>
              <tbody>
                ${items.map((it) =>
                  editing === it.id
                    ? html`<${EditRow} key=${it.id} sectionName=${name} month=${month} initial=${toDraft(name, it)}
                        onSave=${(v) => update(it.id, v)} onCancel=${() => setEditing(null)} />`
                    : html`<${ViewRow} key=${it.id} sectionName=${name} item=${it} month=${month}
                        locked=${editing !== null}
                        onEdit=${() => setEditing(it.id)} onDelete=${() => remove(it)} />`,
                )}
                ${editing === 'new' && html`
                  <${EditRow} key="new" sectionName=${name} month=${month} initial=${{ ...sec.blank }}
                    onSave=${add} onCancel=${() => setEditing(null)} />
                `}
              </tbody>
              ${hasTotals && html`
                <tfoot>
                  <tr>
                    ${sec.columns.map((c, i) => html`
                      <td class=${c.cls}>${i === 0 ? 'Total' : c.total ? money(c.total(data.summary)) : ''}</td>
                    `)}
                    <td class="row-actions"></td>
                  </tr>
                </tfoot>
              `}
            </table>
          </div>
        `}

      <div class="section-foot">
        <button type="button" class="btn btn-sm btn-ghost" disabled=${editing !== null} onClick=${() => setEditing('new')}>
          + Add ${sec.noun}
        </button>
      </div>
    </section>
  `;
}
