// Generic UI pieces: modal dialog, promise-based confirmation, toasts.
import { useEffect, useRef, useState } from 'preact/hooks';
import { html } from './html.js';

/** Native <dialog> driven by the `open` prop: gives focus trapping and Esc for free. */
export function Modal({ open, onClose, title, children, className = '' }) {
  const ref = useRef();
  useEffect(() => {
    const d = ref.current;
    if (open && !d.open) d.showModal();
    if (!open && d.open) d.close();
  }, [open]);

  return html`
    <dialog
      ref=${ref}
      class="modal ${className}"
      onCancel=${(e) => { e.preventDefault(); onClose(); }}
    >
      ${open && html`
        <header class="modal-head">
          <h2>${title}</h2>
          <button type="button" class="icon-btn" aria-label="Close" onClick=${onClose}>×</button>
        </header>
        ${children}
      `}
    </dialog>
  `;
}

// ---------------------------------------------------------------- confirm

let showConfirm = null;

/**
 * Ask the user to confirm something. Resolves true/false.
 * confirmAction({ title, message, confirmLabel = 'Delete', danger = true })
 */
export function confirmAction(opts) {
  return new Promise((resolve) => showConfirm({ confirmLabel: 'Delete', danger: true, ...opts, resolve }));
}

export function ConfirmHost() {
  const [state, setState] = useState(null);
  useEffect(() => { showConfirm = setState; }, []);

  const finish = (answer) => {
    state?.resolve(answer);
    setState(null);
  };

  return html`
    <${Modal} open=${!!state} onClose=${() => finish(false)} title=${state?.title} className="modal-confirm">
      <div class="modal-body">
        ${typeof state?.message === 'string' ? html`<p>${state.message}</p>` : state?.message}
      </div>
      <footer class="modal-actions">
        <button type="button" class="btn" autofocus onClick=${() => finish(false)}>Cancel</button>
        <button type="button" class="btn ${state?.danger ? 'btn-danger' : 'btn-primary'}" onClick=${() => finish(true)}>
          ${state?.confirmLabel}
        </button>
      </footer>
    <//>
  `;
}

// ---------------------------------------------------------------- toasts

let pushToast = null;
let nextToastId = 1;

/** Show a transient message. kind: 'info' | 'success' | 'error'. */
export function toast(message, kind = 'info', ms = kind === 'error' ? 8000 : 4000) {
  pushToast?.({ id: nextToastId++, message, kind, ms });
}

export function ToastHost() {
  const [toasts, setToasts] = useState([]);
  const dismiss = (id) => setToasts((ts) => ts.filter((t) => t.id !== id));

  useEffect(() => {
    pushToast = (t) => {
      setToasts((ts) => [...ts, t]);
      setTimeout(() => dismiss(t.id), t.ms);
    };
  }, []);

  return html`
    <div class="toasts" role="status" aria-live="polite">
      ${toasts.map((t) => html`
        <div key=${t.id} class="toast toast-${t.kind}">
          <div class="toast-msg">${t.message}</div>
          <button type="button" class="icon-btn" aria-label="Dismiss" onClick=${() => dismiss(t.id)}>×</button>
        </div>
      `)}
    </div>
  `;
}
