import { useEffect, useState } from 'preact/hooks';
import { html } from '../html.js';
import { api } from '../api.js';
import { confirmAction, toast } from '../ui.js';
import { dateTime } from '../format.js';
import { Section } from './sections.js';

export function MonthView({ monthId, onChanged, onDeleted }) {
  const [data, setData] = useState(null);
  const [error, setError] = useState(null);

  useEffect(() => {
    let live = true;
    setData(null);
    setError(null);
    api.getMonth(monthId).then(
      (d) => live && setData(d),
      (e) => live && setError(e.message),
    );
    return () => { live = false; };
  }, [monthId]);

  if (error) return html`<div class="card empty-state"><p class="negative">${error}</p></div>`;
  if (!data) return html`<div class="loading">Loading…</div>`;

  /** Mutations return the refreshed month; apply it and let the sidebar refresh. */
  const apply = (fresh) => {
    setData(fresh);
    onChanged();
  };

  async function remove() {
    const ok = await confirmAction({
      title: `Delete ${data.month.label}?`,
      message: html`
        <p>This permanently deletes ${data.month.label} and all of its income, bills, savings pots and debts.</p>
        <p class="muted small">If you need it back later, the app saves a snapshot each time it starts (see README).</p>
      `,
      confirmLabel: 'Delete month',
    });
    if (!ok) return;
    try {
      await api.deleteMonth(monthId);
      toast(`Deleted ${data.month.label}`, 'success');
      onDeleted();
    } catch (e) {
      toast(e.message, 'error');
    }
  }

  return html`
    <article class="month-view">
      <header class="month-head">
        <div>
          <h1>${data.month.label}</h1>
          <p class="muted small">Last changed ${dateTime(data.month.updatedAt)}</p>
        </div>
        <div class="month-actions">
          <button type="button" class="btn btn-danger-ghost" onClick=${remove}>Delete month</button>
        </div>
      </header>

      <div class="sections">
        <${Section} name="income" data=${data} onMonth=${apply} />
        <${Section} name="bills" data=${data} onMonth=${apply} />
        <${Section} name="pots" data=${data} onMonth=${apply} />
        <${Section} name="debts" data=${data} onMonth=${apply} />
      </div>

      <${Notes} key=${monthId} month=${data.month} onSaved=${apply} />
    </article>
  `;
}

function Notes({ month, onSaved }) {
  const [text, setText] = useState(month.notes);
  const [status, setStatus] = useState('');

  async function save() {
    if (text === month.notes) return;
    setStatus('Saving…');
    try {
      onSaved(await api.updateNotes(month.id, text));
      setStatus('Saved');
      setTimeout(() => setStatus(''), 2000);
    } catch (e) {
      setStatus('');
      toast(e.message, 'error');
    }
  }

  return html`
    <section class="card notes">
      <header class="card-head">
        <h2><label for="month-notes">Notes</label></h2>
        <span class="muted small">${status}</span>
      </header>
      <textarea
        id="month-notes"
        rows="3"
        maxlength="5000"
        placeholder="Anything worth remembering about this month (saved when you click away)"
        value=${text}
        onInput=${(e) => setText(e.currentTarget.value)}
        onBlur=${save}
      ></textarea>
    </section>
  `;
}
