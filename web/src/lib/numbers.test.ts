import { describe, expect, it } from 'vitest';
import { cleanNumberInput, parseNumber } from './numbers';

describe('number fields', () => {
  it('keep only what makes a number while typing', () => {
    expect(cleanNumberInput('abc-12.5.5')).toBe('-12.55'); // the second point is dropped
    expect(cleanNumberInput('١٢٫٥ ₪')).toBe('١٢٫٥');
    expect(cleanNumberInput('1,250.75')).toBe('1,250.75');
    expect(cleanNumberInput('12-3')).toBe('123');
    expect(parseNumber(cleanNumberInput('١٢٫٥ ₪'))).toBe(12.5);
  });
});
