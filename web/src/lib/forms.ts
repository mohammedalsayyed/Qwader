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

/**
 * One line to show for a failed request. A validation failure's title is only «correct the marked fields»,
 * which says nothing where no field is marked (toasts, sheets with their own state), so the first field message wins.
 */
export function errorMessage(error: unknown): string {
  if (!(error instanceof ApiError)) return 'حدث خطأ غير متوقع';
  const first = error.fieldErrors ? Object.values(error.fieldErrors)[0]?.[0] : undefined;
  return first ?? error.message;
}

/** Optional text input → null when blank. */
export const blankToNull = (v: string | null | undefined) => (v && v.trim() ? v.trim() : null);
