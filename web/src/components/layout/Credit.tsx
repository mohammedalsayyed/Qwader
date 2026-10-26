import { useQuery } from '@tanstack/react-query';
import { api } from '@/api/client';
import { cn } from '@/lib/cn';

/** Copyright, developer credit and the app version (from the server's App:Version), under the menus and on the sign-in page. */
export function Credit({ className }: { className?: string }) {
  const info = useQuery({ queryKey: ['app-info'], queryFn: () => api<{ version: string }>('/app-info'), staleTime: Infinity, retry: false });

  return (
    <p className={cn('text-center text-xs leading-5 print:hidden', className)}>
      حقوق الطبع محفوظة © {new Date().getFullYear()}
      <br />
      برمجة وتصميم: محمد السيد <a href="tel:+972592541090" className="ltr inline-block font-medium underline-offset-2 hover:underline">00972592541090</a>
      {info.data && (
        <>
          <br />
          <span className="opacity-80">الإصدار <span className="ltr inline-block">{info.data.version}</span></span>
        </>
      )}
    </p>
  );
}
