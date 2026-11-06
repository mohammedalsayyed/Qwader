import { describe, expect, it } from 'vitest';
import { amountInWords } from './tafqeet';

describe('amount in words', () => {
  it('agrees the counted noun with the number', () => {
    expect(amountInWords(40)).toContain('أربعون شيكلًا');
    expect(amountInWords(1)).toContain('شيكل واحد');
    expect(amountInWords(2)).toContain('شيكلان');
    expect(amountInWords(5)).toContain('شواقل');
    expect(amountInWords(100)).toMatch(/مئة شيكل /);
    expect(amountInWords(1250.5)).toContain('شيكلًا و50 أغورة');
  });
});
