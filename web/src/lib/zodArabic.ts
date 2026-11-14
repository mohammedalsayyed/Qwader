import { z } from 'zod';

/**
 * Arabic wording for the checks a form schema does not word itself (a too long text, a number out of range…), so no English
 * «String must contain at most 150 character(s)» ever reaches the screen. Messages a schema gives explicitly still win.
 */
export function setArabicZodMessages() {
  z.setErrorMap((issue, ctx) => {
    switch (issue.code) {
      case z.ZodIssueCode.too_big:
        return { message: issue.type === 'string' ? `النص أطول من المسموح (${issue.maximum} حرفًا على الأكثر)` : `القيمة أكبر من المسموح (${issue.maximum})` };
      case z.ZodIssueCode.too_small:
        return { message: issue.type === 'string' ? (issue.minimum === 1 ? 'هذا الحقل مطلوب' : `النص أقصر من المطلوب (${issue.minimum} أحرف على الأقل)`) : `القيمة أقل من المسموح (${issue.minimum})` };
      case z.ZodIssueCode.invalid_type:
        return { message: issue.received === 'undefined' || issue.received === 'null' ? 'هذا الحقل مطلوب' : 'القيمة المدخلة غير صالحة' };
      case z.ZodIssueCode.invalid_string:
        return { message: 'الصيغة غير صحيحة' };
      case z.ZodIssueCode.invalid_enum_value:
        return { message: 'اختر قيمة من القائمة' };
      default:
        return { message: ctx.defaultError === 'Required' ? 'هذا الحقل مطلوب' : 'القيمة المدخلة غير صالحة' };
    }
  });
}
