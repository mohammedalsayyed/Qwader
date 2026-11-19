import { describe, expect, it } from 'vitest';
import { payUnits, workingDays } from './PayrollPages';

describe('pay units for a period', () => {
  it('counts days, weeks and whole months', () => {
    expect(payUnits('Daily', '2026-11-01', '2026-11-05')).toBe(5);
    expect(payUnits('Weekly', '2026-11-01', '2026-11-14')).toBe(2);
    expect(payUnits('Monthly', '2026-10-01', '2026-10-31')).toBe(1);
    expect(payUnits('Monthly', '2026-02-01', '2026-02-28')).toBe(1);
    expect(payUnits('Monthly', '2026-10-01', '2026-12-31')).toBe(3);
    expect(payUnits('Monthly', '2026-10-16', '2026-10-30')).toBe(0.5);
    expect(payUnits('Daily', '2026-11-05', '2026-11-01')).toBe(0);
  });

  it('counts a week as six working days, Friday off', () => {
    // 2026-11-06 and 2026-11-13 are Fridays.
    expect(workingDays('2026-11-01', '2026-11-07')).toBe(6);
    expect(payUnits('Weekly', '2026-11-01', '2026-11-07')).toBe(1);
    expect(payUnits('Weekly', '2026-11-01', '2026-11-10')).toBe(1.5);
    expect(payUnits('Daily', '2026-11-01', '2026-11-07')).toBe(6);
    expect(workingDays('2026-11-06', '2026-11-06')).toBe(0);
    expect(workingDays('2026-11-07', '2026-11-12')).toBe(6);
  });
});
