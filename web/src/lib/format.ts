// Display formatting. Amounts use Western digits (what staff type on the keypad) with Arabic labels.
export const TIME_ZONE = 'Asia/Hebron';

const moneyFormatter = new Intl.NumberFormat('ar-EG-u-nu-latn', {
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
});

const quantityFormatter = new Intl.NumberFormat('ar-EG-u-nu-latn', { maximumFractionDigits: 3 });

// A piece's cost is kept to 4 decimals (100 ₪ for 3 pieces = 33.3333): shown whole, so a fraction is never hidden by rounding.
const costFormatter = new Intl.NumberFormat('ar-EG-u-nu-latn', { minimumFractionDigits: 2, maximumFractionDigits: 4 });

/** A cost as kept: 52 → "52.00 ₪", 51.9973 → "51.9973 ₪". */
export function formatCost(value: number | null | undefined): string {
  return `${costFormatter.format(value ?? 0)} ₪`;
}

/** 1234.5 → "1,234.50 ₪" */
export function formatMoney(value: number | null | undefined): string {
  return `${moneyFormatter.format(value ?? 0)} ₪`;
}

export function formatQuantity(value: number | null | undefined): string {
  return quantityFormatter.format(value ?? 0);
}

const dateFormatter = new Intl.DateTimeFormat('ar-EG-u-nu-latn', {
  timeZone: TIME_ZONE,
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
});

const dateTimeFormatter = new Intl.DateTimeFormat('ar-EG-u-nu-latn', {
  timeZone: TIME_ZONE,
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
  hour: '2-digit',
  minute: '2-digit',
});

export function formatDate(value: string | Date | null | undefined): string {
  return value ? dateFormatter.format(new Date(value)) : '—';
}

export function formatDateTime(value: string | Date | null | undefined): string {
  return value ? dateTimeFormatter.format(new Date(value)) : '—';
}

/** Today's date in Palestine as yyyy-mm-dd (for date inputs and report filters). */
export function localToday(): string {
  return new Intl.DateTimeFormat('en-CA', { timeZone: TIME_ZONE }).format(new Date());
}
