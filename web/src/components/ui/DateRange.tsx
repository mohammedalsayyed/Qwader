import { useState } from 'react';
import { localToday } from '@/lib/format';
import { FilterChips } from './Feedback';
import { Field, Input } from './Field';

export type RangePreset = 'all' | 'today' | 'yesterday' | 'last7' | 'thisMonth' | 'lastMonth' | 'custom';
export interface DateRangeValue {
  preset: RangePreset;
  from?: string;
  to?: string;
}

const iso = (d: Date) => d.toISOString().slice(0, 10);

/** Resolves a preset to yyyy-mm-dd bounds in Palestine local time. */
export function resolveRange(preset: RangePreset, custom?: { from?: string; to?: string }): { from?: string; to?: string } {
  const today = localToday();
  const t = new Date(`${today}T00:00:00Z`);
  const shift = (days: number) => iso(new Date(t.getTime() + days * 86_400_000));
  switch (preset) {
    case 'today':
      return { from: today, to: today };
    case 'yesterday':
      return { from: shift(-1), to: shift(-1) };
    case 'last7':
      return { from: shift(-6), to: today };
    case 'thisMonth':
      return { from: `${today.slice(0, 8)}01`, to: today };
    case 'lastMonth': {
      const firstThis = new Date(`${today.slice(0, 8)}01T00:00:00Z`);
      const lastPrev = new Date(firstThis.getTime() - 86_400_000);
      return { from: `${iso(lastPrev).slice(0, 8)}01`, to: iso(lastPrev) };
    }
    case 'custom':
      return { from: custom?.from || undefined, to: custom?.to || undefined };
    default:
      return {};
  }
}

const LABELS: Record<RangePreset, string> = {
  all: 'الكل',
  today: 'اليوم',
  yesterday: 'أمس',
  last7: 'آخر 7 أيام',
  thisMonth: 'هذا الشهر',
  lastMonth: 'الشهر السابق',
  custom: 'فترة مخصصة',
};

export function DateRangeFilter({
  value,
  onChange,
  presets = ['all', 'today', 'last7', 'thisMonth', 'lastMonth', 'custom'],
}: {
  value: DateRangeValue;
  onChange: (value: DateRangeValue) => void;
  presets?: RangePreset[];
}) {
  const [custom, setCustom] = useState({ from: value.from ?? '', to: value.to ?? '' });
  const today = localToday();

  return (
    <div className="space-y-2">
      <FilterChips
        value={value.preset}
        onChange={(p) => onChange({ preset: p, ...resolveRange(p, custom) })}
        options={presets.map((p) => ({ value: p, label: LABELS[p] }))}
      />
      {value.preset === 'custom' && (
        <div className="grid grid-cols-2 gap-2">
          <Field label="من تاريخ">
            {(id) => (
              <Input id={id} type="date" max={today} value={custom.from}
                onChange={(e) => { const next = { ...custom, from: e.target.value }; setCustom(next); onChange({ preset: 'custom', ...next }); }} />
            )}
          </Field>
          <Field label="إلى تاريخ">
            {(id) => (
              <Input id={id} type="date" max={today} value={custom.to}
                onChange={(e) => { const next = { ...custom, to: e.target.value }; setCustom(next); onChange({ preset: 'custom', ...next }); }} />
            )}
          </Field>
        </div>
      )}
    </div>
  );
}
