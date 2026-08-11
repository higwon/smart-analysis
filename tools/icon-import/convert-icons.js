const fs = require('fs');
const path = require('path');
const dir = process.argv[2];   // icons dir
const out = process.argv[3];   // output Icons.xaml

// Lucide filename -> SA.Icon key
const MAP = {
  'import': 'Import', 'folder-open': 'FolderOpen', 'save': 'Save', 'git-compare': 'Compare',
  'sliders-horizontal': 'Parameters', 'sigma': 'Statistics', 'crosshair': 'Cursor',
  'maximize': 'ZoomFit', 'palette': 'Colormap', 'ruler': 'Scalebar', 'check': 'Check',
  'triangle-alert': 'Warning', 'circle-alert': 'Error', 'chevron-right': 'ChevronRight',
  'chevron-down': 'ChevronDown', 'image': 'Dataset', 'x': 'Close', 'refresh-cw': 'Refresh',
  'sun-moon': 'Theme', 'sparkles': 'Assistant', 'dot': 'Dot', 'circle': 'Circle',
};

const num = '(-?[0-9]*\\.?[0-9]+)';
const attr = (s, n) => { const m = s.match(new RegExp(n + '\\s*=\\s*"' + num + '"')); return m ? parseFloat(m[1]) : null; };

function circle(cx, cy, r) {
  return `M${cx - r},${cy}a${r},${r} 0 1 0 ${2 * r},0a${r},${r} 0 1 0 ${-2 * r},0Z`;
}
function ellipse(cx, cy, rx, ry) {
  return `M${cx - rx},${cy}a${rx},${ry} 0 1 0 ${2 * rx},0a${rx},${ry} 0 1 0 ${-2 * rx},0Z`;
}
function rect(x, y, w, h, rx, ry) {
  rx = rx || 0; ry = ry || rx;
  if (!rx && !ry) return `M${x},${y}h${w}v${h}h${-w}Z`;
  return `M${x + rx},${y}h${w - 2 * rx}a${rx},${ry} 0 0 1 ${rx},${ry}v${h - 2 * ry}` +
         `a${rx},${ry} 0 0 1 ${-rx},${ry}h${-(w - 2 * rx)}a${rx},${ry} 0 0 1 ${-rx},${-ry}` +
         `v${-(h - 2 * ry)}a${rx},${ry} 0 0 1 ${rx},${-ry}Z`;
}
function pointsToPath(pts, close) {
  const p = pts.trim().split(/[\s,]+/).map(Number);
  let d = `M${p[0]},${p[1]}`;
  for (let i = 2; i < p.length; i += 2) d += `L${p[i]},${p[i + 1]}`;
  return d + (close ? 'Z' : '');
}

function toGeometry(svg) {
  const figs = [];
  // <path d="...">
  for (const m of svg.matchAll(/<path[^>]*\bd\s*=\s*"([^"]+)"/g)) figs.push(m[1].trim());
  // <line>
  for (const m of svg.matchAll(/<line\b[^>]*>/g)) {
    const s = m[0];
    figs.push(`M${attr(s, 'x1')},${attr(s, 'y1')}L${attr(s, 'x2')},${attr(s, 'y2')}`);
  }
  // <circle>
  for (const m of svg.matchAll(/<circle\b[^>]*>/g)) {
    const s = m[0]; figs.push(circle(attr(s, 'cx'), attr(s, 'cy'), attr(s, 'r')));
  }
  // <ellipse>
  for (const m of svg.matchAll(/<ellipse\b[^>]*>/g)) {
    const s = m[0]; figs.push(ellipse(attr(s, 'cx'), attr(s, 'cy'), attr(s, 'rx'), attr(s, 'ry')));
  }
  // <rect>
  for (const m of svg.matchAll(/<rect\b[^>]*>/g)) {
    const s = m[0];
    figs.push(rect(attr(s, 'x') || 0, attr(s, 'y') || 0, attr(s, 'width'), attr(s, 'height'), attr(s, 'rx'), attr(s, 'ry')));
  }
  // <polyline> / <polygon>
  for (const m of svg.matchAll(/<polyline\b[^>]*\bpoints\s*=\s*"([^"]+)"/g)) figs.push(pointsToPath(m[1], false));
  for (const m of svg.matchAll(/<polygon\b[^>]*\bpoints\s*=\s*"([^"]+)"/g)) figs.push(pointsToPath(m[1], true));
  return figs; // each source primitive stays its OWN figure — never string-joined (relative-m safety)
}

const files = fs.readdirSync(dir).filter(f => f.endsWith('.svg'));
const entries = [];
for (const f of files) {
  const name = path.basename(f, '.svg');
  const key = MAP[name];
  if (!key) continue;
  const g = toGeometry(fs.readFileSync(path.join(dir, f), 'utf8'));
  if (!g.length) { console.error('EMPTY', name); continue; }
  entries.push({ key, name, figs: g });
}
entries.sort((a, b) => a.key.localeCompare(b.key));

let xaml = `<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!--
        First-party icon geometries (TASK-UIX04). Vendored from Lucide (https://lucide.dev), ISC license
        (see Icons/LUCIDE-LICENSE.txt, doc 25). Each SVG (24-grid, outline) is converted to a WPF geometry;
        IconPresenter STROKES it with a token brush (currentColor semantics) so icons theme-swap. Keys are
        namespaced SA.Icon.*. Regenerate via tools (doc 25); do not hand-edit path data.
    -->
`;
for (const e of entries) {
  if (e.figs.length === 1) {
    xaml += `    <PathGeometry x:Key="SA.Icon.${e.key}" Figures="${e.figs[0]}"/>\n`;
  } else {
    xaml += `    <GeometryGroup x:Key="SA.Icon.${e.key}" FillRule="Nonzero">\n`;
    for (const fig of e.figs) xaml += `        <PathGeometry Figures="${fig}"/>\n`;
    xaml += `    </GeometryGroup>\n`;
  }
}
xaml += `</ResourceDictionary>\n`;
fs.writeFileSync(out, xaml);
console.log(`wrote ${entries.length} icons -> ${out}`);
console.log(entries.map(e => 'SA.Icon.' + e.key).join(', '));
