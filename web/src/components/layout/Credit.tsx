import { cn } from '@/lib/cn';

/** Copyright and developer credit, shown under the menus and on the sign-in page. */
export function Credit({ className }: { className?: string }) {
  return (
    <p className={cn('text-center text-xs leading-5 print:hidden', className)}>
      حقوق الطبع محفوظة © {new Date().getFullYear()}
      <br />
      برمجة وتصميم: محمد السيد <a href="tel:+972592541090" className="ltr inline-block font-medium underline-offset-2 hover:underline">00972592541090</a>
    </p>
  );
}
