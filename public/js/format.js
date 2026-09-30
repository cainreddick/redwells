// Display helpers (UK conventions). Money maths lives in /shared/calc.js.
import { formatGBP, MONTH_NAMES } from '/shared/calc.js';

export { formatGBP as money };

export function shortMonth(year, month) {
  return `${MONTH_NAMES[month - 1].slice(0, 3)} ${year}`;
}

export function dateTime(iso) {
  if (!iso) return '';
  return new Date(iso).toLocaleString('en-GB', { dateStyle: 'short', timeStyle: 'short' });
}

export function percent(fraction) {
  return `${Math.round(fraction * 100)}%`;
}

export function plural(n, word) {
  return `${n} ${word}${n === 1 ? '' : 's'}`;
}
