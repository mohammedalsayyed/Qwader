import { useMemo, useState } from 'react';

// Validated single-series colors (dataviz validator, light surface): positive teal, negative orange.
const POSITIVE = '#0d9488';
const NEGATIVE = '#c2410c';

export interface BarDatum {
  key: string;
  /** Short axis label (e.g. «21/10»). */
  label: string;
  /** Long label for the readout (e.g. «الثلاثاء 21/10»). */
  title: string;
  value: number;
}

/**
 * Single-series bar chart drawn in SVG. Days run right-to-left like the page. Tapping (or hovering)
 * a column shows its exact value above the chart; the readout doubles as the accessible description.
 */
export function BarChart({ data, format, height = 170, label }: { data: BarDatum[]; format: (v: number) => string; height?: number; label: string }) {
  const [active, setActive] = useState<number | null>(null);
  const { max, min } = useMemo(() => {
    const values = data.map((d) => d.value);
    return { max: Math.max(0, ...values), min: Math.min(0, ...values) };
  }, [data]);

  if (data.length === 0) return null;
  const span = max - min || 1;
  const width = 100; // viewBox units; the SVG stretches to the container
  const plotTop = 6;
  const plotBottom = height - 22;
  const plotHeight = plotBottom - plotTop;
  const zeroY = plotTop + (max / span) * plotHeight;
  const slot = width / data.length;
  const barWidth = Math.max(0.6, Math.min(slot - Math.max(0.4, slot * 0.25), 6));
  const shown = active ?? data.length - 1;
  const labelEvery = Math.ceil(data.length / 7);

  return (
    <figure className="m-0">
      <figcaption className="mb-1 flex items-baseline justify-between gap-2 text-sm" aria-live="polite">
        <span className="text-muted">{data[shown].title}</span>
        <b className="tabular">{format(data[shown].value)}</b>
      </figcaption>
      <svg viewBox={`0 0 ${width} ${height}`} preserveAspectRatio="none" className="h-44 w-full touch-none select-none" role="img" aria-label={label}
        onPointerLeave={() => setActive(null)}>
        {/* recessive grid: top, middle, zero */}
        {[plotTop, plotTop + plotHeight / 2].map((y) => (
          <line key={y} x1={0} x2={width} y1={y} y2={y} stroke="var(--color-line)" strokeWidth={0.3} vectorEffect="non-scaling-stroke" />
        ))}
        <line x1={0} x2={width} y1={zeroY} y2={zeroY} stroke="var(--color-muted)" strokeWidth={1} vectorEffect="non-scaling-stroke" />
        {data.map((d, i) => {
          // Right-to-left: the first day sits at the right edge.
          const cx = width - (i + 0.5) * slot;
          const h = (Math.abs(d.value) / span) * plotHeight;
          const y = d.value >= 0 ? zeroY - h : zeroY;
          const isActive = i === shown;
          return (
            <g key={d.key}>
              <rect x={cx - barWidth / 2} y={y} width={barWidth} height={Math.max(h, d.value === 0 ? 0 : 0.8)}
                rx={Math.min(1, barWidth / 3)} fill={d.value >= 0 ? POSITIVE : NEGATIVE} opacity={active === null || isActive ? 1 : 0.45} />
              {/* hit target: the whole column, wider than the bar */}
              <rect x={cx - slot / 2} y={0} width={slot} height={height} fill="transparent"
                onPointerEnter={() => setActive(i)} onPointerDown={() => setActive(i)} />
              {i % labelEvery === 0 && (
                <text x={cx} y={height - 6} textAnchor="middle" fontSize={3.2} fill="var(--color-muted)" style={{ fontFamily: 'inherit' }}>{d.label}</text>
              )}
            </g>
          );
        })}
      </svg>
    </figure>
  );
}
