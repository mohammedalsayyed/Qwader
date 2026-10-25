import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { Bell, FileText, FolderTree, PackageX, Receipt, Search, TrendingDown, Truck, UserRound } from 'lucide-react';
import { searchApi, type SearchHit, type SearchKind } from '@/api/catalog';
import { useAuth } from '@/auth/AuthProvider';
import { IconButton } from '@/components/ui/Button';
import { EmptyState, ErrorState, SearchInput, Spinner } from '@/components/ui/Feedback';
import { Sheet } from '@/components/ui/Sheet';
import { ProductThumb } from '@/components/catalog/CatalogBits';
import { cn } from '@/lib/cn';
import { errorMessage } from '@/lib/forms';
import { formatMoney, formatQuantity } from '@/lib/format';
import { useDebouncedValue } from '@/lib/hooks';

const GROUPS: { kind: SearchKind; label: string }[] = [
  { kind: 'Product', label: 'المنتجات' },
  { kind: 'Customer', label: 'العملاء' },
  { kind: 'SaleInvoice', label: 'فواتير المبيعات' },
  { kind: 'Category', label: 'الأصناف' },
  { kind: 'Supplier', label: 'الموردون' },
  { kind: 'PurchaseInvoice', label: 'فواتير الشراء' },
];

function hitLink(hit: SearchHit): string {
  switch (hit.kind) {
    case 'Product':
      return `/products/${hit.id}`;
    case 'Category':
      return `/products?categoryId=${hit.id}`;
    case 'Supplier':
      return `/suppliers/${hit.id}`;
    case 'PurchaseInvoice':
      return `/purchases/${hit.id}`;
    case 'Customer':
      return `/customers/${hit.id}`;
    case 'SaleInvoice':
      return `/sales/${hit.id}`;
  }
}

export function GlobalSearchButton() {
  const [open, setOpen] = useState(false);

  // "/" opens search on keyboards (desktop), unless the user is typing in a field.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      const target = e.target as HTMLElement;
      if (e.key === '/' && !['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName)) {
        e.preventDefault();
        setOpen(true);
      }
    };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, []);

  return (
    <>
      <button type="button" onClick={() => setOpen(true)} aria-label="بحث سريع"
        className="flex h-11 items-center gap-2 rounded-xl px-3 text-muted hover:bg-black/5 sm:min-w-56 sm:border sm:border-line sm:bg-canvas">
        <Search className="size-5" />
        <span className="hidden text-sm sm:inline">بحث سريع…</span>
        <kbd className="ms-auto hidden rounded border border-line px-1.5 text-xs lg:inline">/</kbd>
      </button>
      {open && <GlobalSearchSheet onClose={() => setOpen(false)} />}
    </>
  );
}

function GlobalSearchSheet({ onClose }: { onClose: () => void }) {
  const navigate = useNavigate();
  const [q, setQ] = useState('');
  const term = useDebouncedValue(q.trim(), 200);
  const results = useQuery({
    queryKey: ['global-search', term],
    queryFn: ({ signal }) => searchApi.search(term, signal),
    enabled: term.length > 0,
    placeholderData: keepPreviousData,
  });

  const go = (to: string) => {
    onClose();
    navigate(to);
  };

  return (
    <Sheet open onClose={onClose} title="بحث سريع" size="lg">
      <div className="sticky top-0 z-10 -mx-4 bg-surface px-4 pb-3">
        <SearchInput value={q} onChange={setQ} autoFocus placeholder="منتج، كود، باركود، عميل، جوال، رقم فاتورة…" />
      </div>
      {!term ? (
        <p className="py-8 text-center text-sm text-muted">اكتب للبحث في كل أقسام النظام</p>
      ) : results.isPending ? (
        <Spinner />
      ) : results.isError ? (
        <ErrorState message={errorMessage(results.error)} onRetry={() => void results.refetch()} />
      ) : results.data.hits.length === 0 ? (
        <EmptyState title={`لا توجد نتائج لـ «${term}»`} description="جرّب جزءًا من الاسم أو الكود" />
      ) : (
        <div className={cn('space-y-4', results.isFetching && 'opacity-60')}>
          {GROUPS.map(({ kind, label }) => {
            const hits = results.data.hits.filter((h) => h.kind === kind);
            if (hits.length === 0) return null;
            return (
              <section key={kind}>
                <h3 className="mb-1 text-xs font-semibold text-muted">{label}</h3>
                <ul className="divide-y divide-line">
                  {hits.map((h) => (
                    <li key={`${kind}-${h.id}`}>
                      <button type="button" onClick={() => go(hitLink(h))} className="flex w-full items-center gap-3 py-2.5 text-start hover:bg-canvas">
                        {kind === 'Product' ? <ProductThumb fileId={h.imageFileId} size="sm" /> : <HitIcon kind={kind} />}
                        <span className="min-w-0 flex-1">
                          <span className="block truncate font-semibold">{h.title}</span>
                          <span className="block truncate text-xs text-muted">
                            {h.code && <span className="ltr inline-block">{h.code}</span>}{h.code && h.subtitle && ' · '}{h.subtitle}
                          </span>
                        </span>
                        {kind === 'Product' && (
                          <span className="shrink-0 text-end">
                            <span className="tabular block text-sm font-bold">{formatMoney(h.price)}</span>
                            <span className={cn('tabular block text-xs', (h.quantity ?? 0) <= 0 ? 'text-danger' : 'text-muted')}>
                              الكمية {formatQuantity(h.quantity)}
                            </span>
                          </span>
                        )}
                        {kind === 'Category' && <span className="shrink-0 text-xs text-muted">{h.quantity} منتج</span>}
                        {(kind === 'Supplier' || kind === 'PurchaseInvoice' || kind === 'SaleInvoice' || (kind === 'Customer' && (h.price ?? 0) !== 0)) && h.price != null && (
                          <span className="tabular shrink-0 text-sm">{formatMoney(h.price)}</span>
                        )}
                      </button>
                    </li>
                  ))}
                </ul>
              </section>
            );
          })}
        </div>
      )}
    </Sheet>
  );
}

function HitIcon({ kind }: { kind: SearchKind }) {
  const Icon = kind === 'Category' ? FolderTree : kind === 'Supplier' ? Truck : kind === 'Customer' ? UserRound : kind === 'SaleInvoice' ? Receipt : FileText;
  return (
    <span className="flex size-10 shrink-0 items-center justify-center rounded-xl bg-brand-50 text-brand-700">
      <Icon className="size-5" />
    </span>
  );
}

export function NotificationsButton() {
  const { status } = useAuth();
  const navigate = useNavigate();
  const [open, setOpen] = useState(false);
  const query = useQuery({
    queryKey: ['notifications'],
    queryFn: searchApi.notifications,
    enabled: status === 'authenticated',
    refetchInterval: 120_000,
  });
  const n = query.data;
  const total = n ? n.lowStock + n.outOfStock + n.draftPurchases : 0;

  const items = n
    ? [
        n.outOfStock > 0 && { icon: PackageX, tone: 'text-danger', text: `${n.outOfStock} منتج نفد من المخزون`, to: '/products?stock=Out' },
        n.lowStock > 0 && { icon: TrendingDown, tone: 'text-warning', text: `${n.lowStock} منتج أوشك على النفاد`, to: '/products?stock=Low' },
        n.draftPurchases > 0 && { icon: FileText, tone: 'text-brand-700', text: `${n.draftPurchases} فاتورة شراء مسودة لم تُعتمد`, to: '/purchases' },
      ].filter(Boolean) as { icon: typeof Bell; tone: string; text: string; to: string }[]
    : [];

  return (
    <>
      <IconButton label={total ? `الإشعارات (${total})` : 'الإشعارات'} onClick={() => setOpen(true)} className="relative">
        <Bell className="size-6" />
        {total > 0 && (
          <span className="tabular absolute top-1.5 end-1.5 min-w-4.5 rounded-full bg-danger px-1 text-center text-[11px] leading-4.5 font-bold text-white">
            {total > 99 ? '99+' : total}
          </span>
        )}
      </IconButton>
      <Sheet open={open} onClose={() => setOpen(false)} title="الإشعارات">
        {items.length === 0 ? (
          <EmptyState icon={<Bell className="size-8" />} title="لا توجد تنبيهات حاليًا" />
        ) : (
          <ul className="space-y-2">
            {items.map((i) => (
              <li key={i.to}>
                <button type="button" onClick={() => { setOpen(false); navigate(i.to); }}
                  className="flex min-h-14 w-full items-center gap-3 rounded-xl border border-line px-3 text-start hover:bg-canvas">
                  <i.icon className={cn('size-6 shrink-0', i.tone)} />
                  <span className="font-medium">{i.text}</span>
                </button>
              </li>
            ))}
          </ul>
        )}
      </Sheet>
    </>
  );
}
