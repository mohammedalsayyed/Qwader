import { z } from 'zod';
import { toAsciiDigits } from './arabic';

/** Parses user-typed numbers: Arabic-Indic digits, Arabic decimal separator, thousands commas. Blank → undefined. */
export function parseNumber(value: unknown): number | undefined {
  if (typeof value === 'number') return Number.isFinite(value) ? value : undefined;
  if (typeof value !== 'string') return undefined;
  const cleaned = toAsciiDigits(value.trim()).replace(/٫/g, '.').replace(/[,٬\s]/g, '');
  if (cleaned === '') return undefined;
  const n = Number(cleaned);
  return Number.isFinite(n) ? n : Number.NaN;
}

const invalid = 'أدخل رقمًا صحيحًا';

/** Required non-negative number field for zod schemas (accepts the raw input string). */
export const zAmount = (required = 'هذا الحقل مطلوب') =>
  z.preprocess(parseNumber, z.number({ required_error: required, invalid_type_error: invalid }).refine((n) => !Number.isNaN(n), invalid).refine((n) => n >= 0, 'لا يمكن أن تكون القيمة سالبة'));

/** Optional non-negative number (blank allowed). */
export const zOptionalAmount = () =>
  z.preprocess(parseNumber, z.number({ invalid_type_error: invalid }).refine((n) => !Number.isNaN(n), invalid).refine((n) => n >= 0, 'لا يمكن أن تكون القيمة سالبة').optional());

export const zPositive = (required = 'هذا الحقل مطلوب') =>
  z.preprocess(parseNumber, z.number({ required_error: required, invalid_type_error: invalid }).refine((n) => !Number.isNaN(n), invalid).refine((n) => n > 0, 'يجب أن تكون القيمة أكبر من صفر'));

export const round2 = (n: number) => Math.round((n + Number.EPSILON) * 100) / 100;

/** Mirrors CostMath.SuggestPrice on the server: cost × (1 + margin%), rounded up to the next half shekel. */
export function suggestPrice(cost: number, marginPercent: number): number {
  if (!(cost > 0)) return 0;
  return Math.ceil(cost * (1 + marginPercent / 100) * 2) / 2;
}
