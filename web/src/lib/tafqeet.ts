// Amount in Arabic words for printed vouchers: 1250.5 → «ألف ومئتان وخمسون شيكلًا و50 أغورة فقط».
const ONES = ['', 'واحد', 'اثنان', 'ثلاثة', 'أربعة', 'خمسة', 'ستة', 'سبعة', 'ثمانية', 'تسعة', 'عشرة',
  'أحد عشر', 'اثنا عشر', 'ثلاثة عشر', 'أربعة عشر', 'خمسة عشر', 'ستة عشر', 'سبعة عشر', 'ثمانية عشر', 'تسعة عشر'];
const TENS = ['', '', 'عشرون', 'ثلاثون', 'أربعون', 'خمسون', 'ستون', 'سبعون', 'ثمانون', 'تسعون'];
const HUNDREDS = ['', 'مئة', 'مئتان', 'ثلاثمئة', 'أربعمئة', 'خمسمئة', 'ستمئة', 'سبعمئة', 'ثمانمئة', 'تسعمئة'];

function belowThousand(n: number): string {
  const parts: string[] = [];
  const h = Math.floor(n / 100);
  const rest = n % 100;
  if (h) parts.push(HUNDREDS[h]);
  if (rest) {
    if (rest < 20) parts.push(ONES[rest]);
    else {
      const o = rest % 10;
      const t = Math.floor(rest / 10);
      parts.push(o ? `${ONES[o]} و${TENS[t]}` : TENS[t]);
    }
  }
  return parts.join(' و');
}

function scale(n: number, one: string, two: string, few: string, many: string): string {
  if (n === 0) return '';
  if (n === 1) return one;
  if (n === 2) return two;
  if (n <= 10) return `${belowThousand(n)} ${few}`;
  return `${belowThousand(n)} ${many}`;
}

export function numberToArabicWords(value: number): string {
  const n = Math.floor(Math.abs(value));
  if (n === 0) return 'صفر';
  const millions = Math.floor(n / 1_000_000);
  const thousands = Math.floor((n % 1_000_000) / 1000);
  const rest = n % 1000;
  return [
    scale(millions, 'مليون', 'مليونان', 'ملايين', 'مليون'),
    scale(thousands, 'ألف', 'ألفان', 'آلاف', 'ألف'),
    rest ? belowThousand(rest) : '',
  ].filter(Boolean).join(' و');
}

export function amountInWords(amount: number): string {
  const shekels = Math.floor(amount);
  const agorot = Math.round((amount - shekels) * 100);
  const main = `${numberToArabicWords(shekels)} شيكل`;
  return agorot ? `${main} و${agorot} أغورة فقط لا غير` : `${main} فقط لا غير`;
}
