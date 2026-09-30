import { useEffect, useMemo, useState } from 'preact/hooks';
import { html } from '../html.js';
import { api } from '../api.js';
import { Modal, toast } from '../ui.js';
import { money } from '../format.js';
import { addMonths, MONTH_NAMES, monthKey, monthLabel } from '/shared/calc.js';

/** The month after the newest saved one, or the current month if there are none. */
function defaultTarget(months) {
  if (months.length === 0) {
    const now = new Date();
    return { year: now.getFullYear(), month: now.getMonth() + 1 };
  }
  return addMonths(months[0].year, months[0].month, 1);
}

export function NewMonthDialog({ open, months, onClose, onCreated }) {
  const [year, setYear] = useState(0);
  const [month, setMonth] = useState(1);
  const [mode, setMode] = useState('copy');
  const [sourceId, setSourceId] = useState(null);
  const [busy, setBusy] = useState(false);

  // Reset to sensible defaults each time the dialog opens.
  useEffect(() => {
    if (!open) return;
    const t = defaultTarget(months);
    setYear(t.year);
    setMonth(t.month);
    setMode(months.length ? 'copy' : 'blank');
    setSourceId(null);
    setBusy(false);
  }, [open]);

  const yearValid = Number.isInteger(year) && year >= 2000 && year <= 2100;
  const targetKey = yearValid ? monthKey(year, month) : null;
  const existing = months.find((m) => monthKey(m.year, m.month) === targetKey);

  // Only earlier months can be copied forward; months are newest first, so [0] is the latest.
  const candidates = useMemo(
    () => (targetKey ? months.filter((m) => monthKey(m.year, m.month) < targetKey) : []),
    [months, targetKey],
  );
  const source = candidates.find((m) => m.id === sourceId) ?? candidates[0];
  const effectiveMode = candidates.length ? mode : 'blank';
  const canSubmit = yearValid && !existing && !busy;

  async function submit(e) {
    e.preventDefault();
    if (!canSubmit) return;
    setBusy(true);
    try {
      const created = await api.createMonth({
        year,
        month,
        copyFrom: effectiveMode === 'copy' ? source.id : undefined,
      });
      onCreated(created);
      reportCopy(created);
    } catch (err) {
      toast(err.message, 'error');
      setBusy(false);
    }
  }

  return html`
    <${Modal} open=${open} onClose=${onClose} title="Create a new month">
      <form onSubmit=${submit}>
        <div class="modal-body stack">
          <div class="field-row">
            <label class="field">
              <span class="field-label">Month</span>
              <select value=${month} onChange=${(e) => setMonth(Number(e.currentTarget.value))}>
                ${MONTH_NAMES.map((name, i) => html`<option value=${i + 1}>${name}</option>`)}
              </select>
            </label>
            <label class="field field-narrow">
              <span class="field-label">Year</span>
              <input
                type="number" min="2000" max="2100" step="1" required
                value=${year}
                onInput=${(e) => setYear(Number(e.currentTarget.value))}
                aria-invalid=${!yearValid}
              />
            </label>
          </div>
          ${!yearValid && html`<p class="field-error">Year must be between 2000 and 2100.</p>`}
          ${existing && html`
            <p class="field-error">
              ${existing.label} already exists. <a href="#/${targetKey}" onClick=${onClose}>Open it</a>
            </p>
          `}

          <fieldset class="choice-group">
            <legend class="field-label">Start from</legend>
            <label class="choice ${candidates.length ? '' : 'disabled'}">
              <input
                type="radio" name="mode" value="copy"
                checked=${effectiveMode === 'copy'}
                disabled=${!candidates.length}
                onChange=${() => setMode('copy')}
              />
              <span>
                <strong>Copy forward from </strong>
                ${candidates.length
                  ? html`
                    <select
                      value=${source?.id}
                      onChange=${(e) => { setSourceId(Number(e.currentTarget.value)); setMode('copy'); }}
                    >
                      ${candidates.map((m) => html`<option value=${m.id}>${m.label}</option>`)}
                    </select>`
                  : html`<span class="muted">(no earlier months)</span>`}
                <span class="choice-help">
                  Recurring income and bills are copied. Each savings pot and debt starts from last
                  month's closing balance. Everything stays editable.
                </span>
              </span>
            </label>
            <label class="choice">
              <input type="radio" name="mode" value="blank" checked=${effectiveMode === 'blank'} onChange=${() => setMode('blank')} />
              <span>
                <strong>Start blank</strong>
                <span class="choice-help">An empty month with no income, bills, pots or debts.</span>
              </span>
            </label>
          </fieldset>
        </div>
        <footer class="modal-actions">
          <button type="button" class="btn" onClick=${onClose}>Cancel</button>
          <button type="submit" class="btn btn-primary" disabled=${!canSubmit}>
            ${busy ? 'Creating…' : yearValid ? `Create ${monthLabel(year, month)}` : 'Create'}
          </button>
        </footer>
      </form>
    <//>
  `;
}

function reportCopy({ month, copyReport: r }) {
  if (!r) return toast(`Created ${month.label}`, 'success');
  const notes = [];
  if (r.skippedIncome.length) notes.push(`One-off income not copied: ${r.skippedIncome.join(', ')}.`);
  if (r.paidOffDebts.length) notes.push(`Paid off, so not carried: ${r.paidOffDebts.join(', ')}.`);
  for (const a of r.adjustedPayments) {
    notes.push(`${a.name} payment reduced from ${money(a.from)} to ${money(a.to)} to clear the balance.`);
  }
  toast(
    html`<strong>Created ${month.label} from ${r.source}.</strong>${notes.map((n) => html`<br />${n}`)}`,
    'success',
    notes.length ? 10000 : 4000,
  );
}
