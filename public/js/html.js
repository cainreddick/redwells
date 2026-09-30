// JSX-like tagged templates without a build step: html`<div class=${cls}>…</div>`
import { h } from 'preact';
import htm from 'htm';

export const html = htm.bind(h);
