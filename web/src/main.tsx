import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { isTestCopy } from '@/components/layout/TestBanner';
import { RouterProvider } from 'react-router';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ApiError } from '@/api/client';
import { AuthProvider } from '@/auth/AuthProvider';
import { ToastProvider } from '@/components/ui/Toast';
import { ConfirmProvider } from '@/components/ui/Confirm';
import { router } from '@/app/router';
import { captureInstallPrompt, UpdatePrompt } from '@/app/pwa';
import '@/styles/index.css';

captureInstallPrompt();

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      refetchOnWindowFocus: false,
      // Retry only transient failures (network / server), never validation or permission errors.
      retry: (count, error) => count < 2 && error instanceof ApiError && (error.isNetwork || error.status >= 500),
    },
    // Offline, a save runs at once: the till keeps the sale on the device, and other saves report the missing connection instead of waiting silently.
    mutations: { retry: false, networkMode: 'always' },
  },
});

// The trial copy says so in the browser tab too.
if (isTestCopy()) document.title = `تجريبي — ${document.title}`;

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <ToastProvider>
          <ConfirmProvider>
            <RouterProvider router={router} />
            <UpdatePrompt />
          </ConfirmProvider>
        </ToastProvider>
      </AuthProvider>
    </QueryClientProvider>
  </StrictMode>,
);
