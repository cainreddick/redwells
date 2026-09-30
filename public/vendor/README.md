# Vendored libraries

These are copied verbatim from the npm packages, so the app works offline and needs no
build step or `npm install`. Only the trailing `sourceMappingURL` comments were removed.

| File | Package | Source path in package |
|---|---|---|
| `preact.module.js` | preact 10.29.8 | `dist/preact.module.js` |
| `hooks.module.js` | preact 10.29.8 | `hooks/dist/hooks.module.js` |
| `htm.module.js` | htm 3.1.1 | `dist/htm.module.js` |
| `chart.umd.min.js` | chart.js 4.5.1 | `dist/chart.umd.min.js` (loaded on demand by the Charts page; sets `window.Chart`) |

The import map in `public/index.html` maps the bare specifiers `preact`, `preact/hooks`
and `htm` to these files. To upgrade, `npm pack <pkg>@<version>`, copy the same paths, and
update this table and `LICENSES.txt`.
