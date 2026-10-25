// Mirrors Qwader.Application.Common.ArabicText.Normalize so client-side filtering matches server search.
const map: Record<string, string> = {
  'أ': 'ا',
  'إ': 'ا',
  'آ': 'ا',
  'ٱ': 'ا',
  'ة': 'ه',
  'ى': 'ي',
  'ؤ': 'و',
  'ئ': 'ي',
};

export function normalizeArabic(input: string | null | undefined): string {
  if (!input) return '';
  let out = '';
  let lastSpace = false;
  for (const raw of input.trim()) {
    const code = raw.charCodeAt(0);
    let c = map[raw] ?? raw;
    if (code >= 0x0660 && code <= 0x0669) c = String(code - 0x0660);
    else if (code >= 0x06f0 && code <= 0x06f9) c = String(code - 0x06f0);
    else c = c.toLowerCase();

    const cc = c.charCodeAt(0);
    if ((cc >= 0x064b && cc <= 0x065f) || cc === 0x0670 || c === 'ـ') continue;

    if (/\s/.test(c)) {
      if (!lastSpace) out += ' ';
      lastSpace = true;
      continue;
    }
    lastSpace = false;
    out += c;
  }
  return out;
}

/** Converts Arabic-Indic digits typed on Arabic keyboards to ASCII so numeric inputs parse. */
export function toAsciiDigits(value: string): string {
  return value.replace(/[٠-٩]/g, (d) => String(d.charCodeAt(0) - 0x0660)).replace(/[۰-۹]/g, (d) => String(d.charCodeAt(0) - 0x06f0));
}
