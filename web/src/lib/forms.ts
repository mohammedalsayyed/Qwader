import type { FieldValues, Path, UseFormSetError } from 'react-hook-form';
import { ApiError } from '@/api/client';

/**
 * Puts server field errors next to their inputs. Returns the message to toast when the error
 * is not tied to a field (or concerns a field the form does not show), otherwise null.
 */
export function applyServerErrors<T extends FieldValues>(error: unknown, setError: UseFormSetError<T>, fields: readonly Path<T>[]): string | null {
  if (!(error instanceof ApiError)) return 'حدث خطأ غير متوقع';

  let unmatched = false;
  if (error.fieldErrors) {
    for (const [key, messages] of Object.entries(error.fieldErrors)) {
      if ((fields as readonly string[]).includes(key)) setError(key as Path<T>, { type: 'server', message: messages[0] });
      else unmatched = true;
    }
    if (!unmatched) return null;
    const first = Object.values(error.fieldErrors)[0]?.[0];
    return first ?? error.message;
  }
  return error.message;
}

export function errorMessage(error: unknown): string {
  return error instanceof ApiError ? error.message : 'حدث خطأ غير متوقع';
}

/** Optional text input → null when blank. */
export const blankToNull = (v: string | null | undefined) => (v && v.trim() ? v.trim() : null);
