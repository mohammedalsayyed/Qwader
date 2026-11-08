import { Plus, Sparkles, Trash2 } from 'lucide-react';
import type { ProductUnit, SaveProductUnit } from '@/api/catalog';
import { Button, IconButton } from '@/components/ui/Button';
import { Field, Input } from '@/components/ui/Field';
import { UnitSelect } from '@/components/catalog/CatalogBits';
import { formatMoney, formatQuantity } from '@/lib/format';
import { parseNumber, round2 } from '@/lib/numbers';
import { useLookups } from '@/lib/queries';

/** One extra unit as typed (strings, like the rest of the form). */
export interface UnitRow {
  unitId: number | '';
  factor: string;
  retailPrice: string;
  wholesalePrice: string;
  minSalePrice: string;
  error?: string;
}

export const toUnitRows = (units: ProductUnit[] | null | undefined): UnitRow[] =>
  (units ?? []).map((u) => ({
    unitId: u.unitId, factor: String(u.factor), retailPrice: String(u.retailPrice),
    wholesalePrice: u.wholesalePrice === u.retailPrice ? '' : String(u.wholesalePrice),
    minSalePrice: u.minSalePrice ? String(u.minSalePrice) : '',
  }));

/** Checks the rows; returns them with their errors, and the request when everything is valid. */
export function validateUnitRows(rows: UnitRow[], baseUnitId: number): { rows: UnitRow[]; units: SaveProductUnit[] | null } {
  const units: SaveProductUnit[] = [];
  let ok = true;
  const checked = rows.map((row) => {
    const factor = parseNumber(row.factor);
    const retail = parseNumber(row.retailPrice);
    const wholesale = parseNumber(row.wholesalePrice);
    const min = parseNumber(row.minSalePrice);
    let error: string | undefined;
    if (!row.unitId) error = 'اختر الوحدة';
    else if (row.unitId === baseUnitId) error = 'اختر وحدة غير الوحدة الأساسية';
    else if (rows.filter((r) => r.unitId === row.unitId).length > 1) error = 'هذه الوحدة مكررة';
    else if (factor == null || Number.isNaN(factor) || factor <= 1) error = 'اكتب كم قطعة في هذه الوحدة (أكثر من 1)';
    else if (retail == null || Number.isNaN(retail) || retail <= 0) error = 'اكتب سعر بيع الوحدة';
    else if (wholesale != null && (Number.isNaN(wholesale) || wholesale < 0)) error = 'سعر الجملة غير صحيح';
    else if (min != null && (Number.isNaN(min) || min < 0 || min > retail)) error = 'أقل سعر يجب ألا يتجاوز سعر البيع';
    if (error) ok = false;
    else units.push({ unitId: row.unitId as number, factor: factor!, retailPrice: retail!, wholesalePrice: wholesale ?? retail!, minSalePrice: min ?? 0 });
    return { ...row, error };
  });
  return { rows: checked, units: ok ? units : null };
}

/**
 * «يُباع أيضًا بـ»: the same product sold (and bought) in a bigger unit — a dozen of 12 pieces. Stock stays in the base unit;
 * the dozen's price is typed, with a suggestion of 12 × the piece's price.
 */
export function ProductUnitsEditor({ rows, onChange, baseUnitId, baseRetail, baseWholesale, disabled }: {
  rows: UnitRow[];
  onChange: (rows: UnitRow[]) => void;
  baseUnitId: number;
  baseRetail: number;
  baseWholesale: number;
  disabled?: boolean;
}) {
  const units = useLookups().data?.units ?? [];
  const baseName = units.find((u) => u.id === baseUnitId)?.name ?? 'قطعة';
  const set = (i: number, patch: Partial<UnitRow>) => onChange(rows.map((r, j) => (j === i ? { ...r, ...patch, error: undefined } : r)));
  const addRow = () => {
    const used = new Set([baseUnitId, ...rows.map((r) => r.unitId)]);
    const dozen = units.find((u) => u.name === 'دزينة' && !used.has(u.id));
    const first = dozen ?? units.find((u) => !used.has(u.id));
    onChange([...rows, { unitId: first?.id ?? '', factor: dozen ? '12' : '', retailPrice: '', wholesalePrice: '', minSalePrice: '' }]);
  };

  return (
    <div className="space-y-3">
      <p className="text-sm text-muted">
        المخزون يُحسب بالـ«{baseName}». أضف وحدة أكبر إذا كان المنتج يُباع أو يُشترى بها أيضًا (مثل دزينة = 12 {baseName}).
        إذا كان المنتج يُباع بالدزينة فقط فاجعل «وحدة القياس» نفسها دزينة ولا تضف شيئًا هنا.
      </p>
      {rows.map((row, i) => {
        const factor = parseNumber(row.factor);
        const unitName = units.find((u) => u.id === row.unitId)?.name ?? 'الوحدة';
        const suggestRetail = factor && factor > 1 && baseRetail > 0 ? round2(factor * baseRetail) : null;
        const suggestWholesale = factor && factor > 1 && baseWholesale > 0 ? round2(factor * baseWholesale) : null;
        return (
          <div key={i} className="space-y-3 rounded-xl border border-line p-3">
            {row.error && <p role="alert" className="rounded-lg bg-red-50 p-2 text-sm text-danger">{row.error}</p>}
            <div className="grid grid-cols-[1fr_1fr_auto] items-end gap-3">
              <Field label="الوحدة">
                {(id) => <UnitSelect id={id} value={row.unitId} onChange={(unitId) => set(i, { unitId })} />}
              </Field>
              <Field label={`كم ${baseName} فيها؟`}>
                {(id) => <Input id={id} inputMode="decimal" className="ltr" placeholder="12" disabled={disabled} value={row.factor}
                  onChange={(e) => set(i, { factor: e.target.value })} />}
              </Field>
              <IconButton label={`حذف وحدة ${unitName}`} className="mb-0.5 border border-line" disabled={disabled}
                onClick={() => onChange(rows.filter((_, j) => j !== i))}>
                <Trash2 className="size-5 text-danger" />
              </IconButton>
            </div>
            <div className="grid grid-cols-3 gap-3">
              <Field label={`سعر ${unitName}`}>
                {(id) => <Input id={id} inputMode="decimal" className="ltr" disabled={disabled} value={row.retailPrice}
                  placeholder={suggestRetail ? String(suggestRetail) : '0'} onChange={(e) => set(i, { retailPrice: e.target.value })} />}
              </Field>
              <Field label="سعر الجملة" hint="فارغ = سعر البيع">
                {(id, d) => <Input id={id} aria-describedby={d} inputMode="decimal" className="ltr" disabled={disabled} value={row.wholesalePrice}
                  placeholder={row.retailPrice || '0'} onChange={(e) => set(i, { wholesalePrice: e.target.value })} />}
              </Field>
              <Field label="أقل سعر" hint="فارغ = بدون حد">
                {(id, d) => <Input id={id} aria-describedby={d} inputMode="decimal" className="ltr" disabled={disabled} value={row.minSalePrice}
                  placeholder="0" onChange={(e) => set(i, { minSalePrice: e.target.value })} />}
              </Field>
            </div>
            {suggestRetail != null && !disabled && parseNumber(row.retailPrice) !== suggestRetail && (
              <Button size="sm" variant="secondary"
                onClick={() => set(i, { retailPrice: String(suggestRetail), wholesalePrice: row.wholesalePrice || (suggestWholesale && suggestWholesale !== suggestRetail ? String(suggestWholesale) : '') })}>
                <Sparkles className="size-4" /> اقتراح {formatMoney(suggestRetail)} ({formatQuantity(factor!)} × {formatMoney(baseRetail)})
              </Button>
            )}
          </div>
        );
      })}
      {!disabled && (
        <Button variant="outline" onClick={addRow}>
          <Plus className="size-4" /> إضافة وحدة بيع (مثل دزينة)
        </Button>
      )}
    </div>
  );
}
