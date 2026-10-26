import { describe, expect, it } from 'vitest';
import { ApiError } from '@/api/client';
import { errorMessage } from './forms';

describe('errorMessage', () => {
  it('shows the field message rather than the generic validation title', () => {
    const e = new ApiError(400, 'يرجى تصحيح الحقول المشار إليها', { referenceNo: ['رقم المرجع «X» مسجّل من قبل في الحوالة TR-2026-000001'] });
    expect(errorMessage(e)).toBe('رقم المرجع «X» مسجّل من قبل في الحوالة TR-2026-000001');
  });

  it('keeps a business-rule message as it is', () => {
    expect(errorMessage(new ApiError(422, 'الكمية المتوفرة هي 1 فقط'))).toBe('الكمية المتوفرة هي 1 فقط');
  });
});
