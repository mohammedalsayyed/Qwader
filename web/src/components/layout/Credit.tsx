import { useQuery } from '@tanstack/react-query';
import { api } from '@/api/client';
import { useAuth } from '@/auth/AuthProvider';
import { cn } from '@/lib/cn';

/**
 * Copyright and developer credit, under the menus and on the sign-in page; the version (App:Version) only once signed in —
 * the server does not tell it before sign-in.
 */
export function Credit({ className }: { className?: string }) {
  const { status } = useAuth();
  const info = useQuery({ queryKey: ['app-info'], queryFn: () => api<{ version: string }>('/app-info'), staleTime: Infinity, retry: false,
    enabled: status === 'authenticated' });

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
