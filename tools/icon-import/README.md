# icon-import — Lucide → WPF `SA.Icon.*` geometries

Regenerates/extends the first-party icon set (TASK-UIX04, [ADR-019](../../docs/ai-context/adr/ADR-019-iconography.md),
[doc 25](../../docs/target-design/25-iconography.md)). Icons are **vendored** from
[Lucide](https://lucide.dev) (ISC license) as WPF `Geometry` resources — not a runtime dependency.

## How it works
`convert-icons.js` reads a folder of Lucide SVGs and emits
`src/SmartAnalysis.UI/DesignSystem/Icons/Icons.xaml`. It converts each SVG **primitive**
(`path`/`line`/`circle`/`rect`/`polyline`/`polygon`) into its **own** `PathGeometry` (a `GeometryGroup`
when an icon has several) — never string-joined, so a relative `m` move can't be mis-anchored to a prior
figure's end. The `MAP` at the top maps Lucide filenames → `SA.Icon.*` keys.

## Regenerate / add an icon
```bash
# 1. fetch the Lucide SVG(s) you need (add names to the list)
mkdir -p icons
for n in save import compare-… ; do
  curl -s "https://raw.githubusercontent.com/lucide-icons/lucide/main/icons/$n.svg" -o "icons/$n.svg"
done
# 2. add the filename -> SA.Icon.Key entry to MAP in convert-icons.js, then:
node convert-icons.js ./icons ../../src/SmartAnalysis.UI/DesignSystem/Icons/Icons.xaml
```
Then `dotnet build` and (optional) render a verification sheet. Do **not** hand-edit path data; keep the
ISC notice at `Icons/LUCIDE-LICENSE.txt`. `DesignSystemStyleTests` guards that keys exist and geometries
are non-empty.
