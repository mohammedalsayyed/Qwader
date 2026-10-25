import { useEffect, useRef, useState } from 'react';
import { Camera, FileText, ImagePlus, Loader2, Paperclip, Trash2 } from 'lucide-react';
import { filesApi, type FilePurpose } from '@/api/catalog';
import { fetchPrivateFile } from '@/api/finance';
import type { AccountType } from '@/api/endpoints';
import { useToast } from '@/components/ui/Toast';
import { cn } from '@/lib/cn';
import { errorMessage } from '@/lib/forms';
import { formatDateTime, formatMoney } from '@/lib/format';
import { amountInWords } from '@/lib/tafqeet';

export function AccountChips({
  accounts,
  value,
  onChange,
  label = 'الحساب',
  error,
}: {
  accounts: { id: number; name: string; type: AccountType }[];
  value: number | null;
  onChange: (id: number) => void;
  label?: string;
  error?: string;
}) {
  return (
    <fieldset>
      <legend className="mb-2 text-sm font-medium">{label} <span className="text-danger">*</span></legend>
      <div className="grid grid-cols-3 gap-2" role="radiogroup">
        {accounts.map((a) => (
          <button key={a.id} type="button" role="radio" aria-checked={value === a.id} onClick={() => onChange(a.id)}
            className={cn('min-h-12 rounded-xl border px-2 text-sm font-semibold leading-tight',
              value === a.id ? 'border-brand-700 bg-brand-50 text-brand-800' : error ? 'border-danger' : 'border-line')}>
            {a.name}
          </button>
        ))}
      </div>
      {error && <p className="mt-1 text-sm text-danger">{error}</p>}
    </fieldset>
  );
}

/** Downscales camera photos (phones produce several MB) before upload; PDFs go as-is. */
async function shrink(file: File): Promise<Blob> {
  if (!file.type.startsWith('image/')) return file;
  const bitmap = await createImageBitmap(file).catch(() => null);
  if (!bitmap) return file;
  const scale = Math.min(1, 1600 / Math.max(bitmap.width, bitmap.height));
  if (scale === 1 && file.size < 800_000) return file;
  const canvas = document.createElement('canvas');
  canvas.width = Math.round(bitmap.width * scale);
  canvas.height = Math.round(bitmap.height * scale);
  canvas.getContext('2d')!.drawImage(bitmap, 0, 0, canvas.width, canvas.height);
  return new Promise((resolve) => canvas.toBlob((b) => resolve(b ?? file), 'image/jpeg', 0.85));
}

/** Proof/attachment from the phone camera, the gallery, or a PDF. */
export function AttachmentPicker({
  value,
  onChange,
  purpose,
  label = 'المرفق',
}: {
  value: string | null;
  onChange: (fileId: string | null) => void;
  purpose: FilePurpose;
  label?: string;
}) {
  const camera = useRef<HTMLInputElement>(null);
  const gallery = useRef<HTMLInputElement>(null);
  const [busy, setBusy] = useState(false);
  const toast = useToast();

  const upload = async (file?: File) => {
    if (!file) return;
    setBusy(true);
    try {
      const blob = await shrink(file);
      const name = blob === file ? file.name : file.name.replace(/\.\w+$/, '') + '.jpg';
      onChange((await filesApi.upload(blob, name, purpose)).id);
    } catch (e) {
      toast.error(errorMessage(e));
    } finally {
      setBusy(false);
      if (camera.current) camera.current.value = '';
      if (gallery.current) gallery.current.value = '';
    }
  };

  return (
    <div>
      <p className="mb-2 text-sm font-medium">{label}</p>
      <input ref={camera} type="file" accept="image/*" capture="environment" hidden onChange={(e) => void upload(e.target.files?.[0])} />
      <input ref={gallery} type="file" accept="image/jpeg,image/png,image/webp,application/pdf" hidden onChange={(e) => void upload(e.target.files?.[0])} />
      {value ? (
        <div className="flex items-center gap-3">
          <PrivateFileThumb fileId={value} />
          <button type="button" onClick={() => onChange(null)} className="flex h-10 items-center gap-1 text-sm text-danger">
            <Trash2 className="size-4" /> إزالة
          </button>
        </div>
      ) : (
        <div className="grid grid-cols-2 gap-2">
          <button type="button" disabled={busy} onClick={() => camera.current?.click()} className="flex h-12 items-center justify-center gap-2 rounded-xl border border-line text-sm font-medium">
            {busy ? <Loader2 className="size-4 animate-spin" /> : <Camera className="size-4" />} الكاميرا
          </button>
          <button type="button" disabled={busy} onClick={() => gallery.current?.click()} className="flex h-12 items-center justify-center gap-2 rounded-xl border border-line text-sm font-medium">
            <ImagePlus className="size-4" /> صورة أو PDF
          </button>
        </div>
      )}
    </div>
  );
}

/** Thumbnail of a protected file; tapping opens it full size. */
export function PrivateFileThumb({ fileId, large }: { fileId: string; large?: boolean }) {
  const [url, setUrl] = useState<string | null>(null);
  const [type, setType] = useState<string>('');
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    let revoked: string | null = null;
    fetchPrivateFile(fileId)
      .then(async (u) => {
        revoked = u;
        setType((await fetch(u).then((r) => r.blob())).type);
        setUrl(u);
      })
      .catch(() => setFailed(true));
    return () => {
      if (revoked) URL.revokeObjectURL(revoked);
    };
  }, [fileId]);

  const box = large ? 'h-64 w-full' : 'size-20';
  if (failed) return <span className={cn('flex items-center justify-center rounded-xl bg-canvas text-xs text-muted', box)}>تعذر التحميل</span>;
  if (!url) return <span className={cn('flex items-center justify-center rounded-xl bg-canvas', box)}><Loader2 className="size-5 animate-spin text-muted" /></span>;
  return (
    <a href={url} target="_blank" rel="noopener" className="block" aria-label="فتح المرفق">
      {type === 'application/pdf' ? (
        <span className={cn('flex flex-col items-center justify-center gap-1 rounded-xl border border-line bg-canvas text-sm', box)}>
          <FileText className="size-8 text-brand-700" /> PDF
        </span>
      ) : (
        <img src={url} alt="مرفق" className={cn('rounded-xl bg-canvas', large ? 'h-64 w-full object-contain' : 'size-20 object-cover')} />
      )}
    </a>
  );
}

export function AttachmentBadge() {
  return <Paperclip className="inline size-4 text-muted" aria-label="له مرفق" />;
}

/** A4 printout shared by receipt and payment vouchers. */
export function PrintableVoucher({
  kind,
  voucher,
  store,
}: {
  kind: 'receipt' | 'payment';
  voucher: { number: string; date: string; partyName: string; partyPhone: string | null; amount: number; accountName: string; purposeName: string; reference: string | null; notes: string | null; createdByName: string | null; status: string };
  store?: { storeName: string; storeAddress: string | null; storePhone: string | null };
}) {
  const title = kind === 'receipt' ? 'سند قبض' : 'سند صرف';
  return (
    <article className="hidden text-[14px] text-black print:block">
      <header className="flex items-start justify-between border-b-2 border-black pb-3">
        <div>
          <p className="text-xl font-bold">{store?.storeName}</p>
          {store?.storeAddress && <p>{store.storeAddress}</p>}
          {store?.storePhone && <p className="ltr text-end">{store.storePhone}</p>}
        </div>
        <div className="text-end">
          <p className="text-2xl font-bold">{title}</p>
          <p className="ltr">{voucher.number}</p>
          <p>{formatDateTime(voucher.date)}</p>
          {voucher.status === 'Voided' && <p className="font-bold">ملغى</p>}
        </div>
      </header>
      <div className="mt-6 space-y-4 leading-8">
        <p>{kind === 'receipt' ? 'استلمنا من' : 'صرفنا إلى'}: <b>{voucher.partyName}</b>{voucher.partyPhone && <span className="ltr ms-2 inline-block">{voucher.partyPhone}</span>}</p>
        <p>مبلغًا وقدره: <b className="tabular">{formatMoney(voucher.amount)}</b></p>
        <p className="rounded border border-black p-2">{amountInWords(voucher.amount)}</p>
        <p>وذلك عن: {voucher.purposeName}{voucher.notes && ` — ${voucher.notes}`}</p>
        <p>{kind === 'receipt' ? 'أُودع في' : 'دُفع من'}: {voucher.accountName}{voucher.reference && <> · المرجع: <span className="ltr inline-block">{voucher.reference}</span></>}</p>
      </div>
      <footer className="mt-16 grid grid-cols-2 gap-8 text-center">
        <div><p className="border-t border-black pt-2">المستلم</p></div>
        <div><p className="border-t border-black pt-2">المحاسب: {voucher.createdByName}</p></div>
      </footer>
    </article>
  );
}
