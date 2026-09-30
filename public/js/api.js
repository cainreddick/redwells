// Thin wrapper over the JSON API. Throws ApiError with per-field `details` on validation failures.

export class ApiError extends Error {
  constructor(status, message, details) {
    super(message);
    this.status = status;
    this.details = details;
  }
}

async function request(method, path, body) {
  let res;
  try {
    res = await fetch(path, {
      method,
      headers: body === undefined ? {} : { 'Content-Type': 'application/json' },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
  } catch {
    throw new ApiError(0, "Can't reach the budget server. Is `npm start` still running?");
  }
  const data = (res.headers.get('content-type') ?? '').includes('json') ? await res.json() : null;
  if (!res.ok) throw new ApiError(res.status, data?.error ?? `Request failed (${res.status})`, data?.details);
  return data;
}

export const api = {
  listMonths: () => request('GET', '/api/months'),
  getMonth: (id) => request('GET', `/api/months/${id}`),
  createMonth: ({ year, month, copyFrom }) => request('POST', '/api/months', { year, month, copyFrom }),
  updateNotes: (id, notes) => request('PATCH', `/api/months/${id}`, { notes }),
  deleteMonth: (id) => request('DELETE', `/api/months/${id}`),
  addItem: (monthId, section, item) => request('POST', `/api/months/${monthId}/${section}`, item),
  updateItem: (section, id, item) => request('PUT', `/api/${section}/${id}`, item),
  deleteItem: (section, id) => request('DELETE', `/api/${section}/${id}`),
};
