import { useEffect, useRef, useState } from 'react';
import { Camera, ScanBarcode } from 'lucide-react';
import { Button, IconButton } from './Button';
import { Sheet } from './Sheet';

interface ScannerProps {
  onDetected: (code: string) => void;
  onClose: () => void;
  /** Keep the camera open after a read (POS: scan item after item). */
  continuous?: boolean;
  title?: string;
}

type ScannerControls = { stop: () => void };

/**
 * Reads product barcodes (EAN/UPC/Code128…) with the phone's back camera. The decoder library is loaded
 * only when the scanner opens. Requires HTTPS, which the public address provides.
 */
export function BarcodeScannerSheet({ onDetected, onClose, continuous = false, title = 'مسح الباركود' }: ScannerProps) {
  const videoRef = useRef<HTMLVideoElement>(null);
  const [error, setError] = useState<string | null>(null);
  const [last, setLast] = useState<string | null>(null);
  const lastRead = useRef<{ code: string; at: number } | null>(null);
  const onDetectedRef = useRef(onDetected);
  onDetectedRef.current = onDetected;

  useEffect(() => {
    let controls: ScannerControls | null = null;
    let cancelled = false;

    (async () => {
      if (!navigator.mediaDevices?.getUserMedia) {
        setError('المتصفح لا يدعم الكاميرا. افتح النظام من الرابط الآمن (https) أو من التطبيق المثبت.');
        return;
      }
      try {
        const { BrowserMultiFormatReader } = await import('@zxing/browser');
        if (cancelled || !videoRef.current) return;
        const reader = new BrowserMultiFormatReader();
        controls = await reader.decodeFromConstraints(
          { video: { facingMode: { ideal: 'environment' }, width: { ideal: 1280 } }, audio: false },
          videoRef.current,
          (result) => {
            if (!result) return;
            const code = result.getText().trim();
            const now = Date.now();
            // The same code is read many times per second; accept it once per 1.5 s.
            if (lastRead.current && lastRead.current.code === code && now - lastRead.current.at < 1500) return;
            lastRead.current = { code, at: now };
            navigator.vibrate?.(80);
            setLast(code);
            onDetectedRef.current(code);
            if (!continuous) {
              controls?.stop();
              onClose();
            }
          },
        );
        if (cancelled) controls.stop();
      } catch (e) {
        const name = (e as { name?: string })?.name;
        setError(
          name === 'NotAllowedError'
            ? 'تم رفض الوصول إلى الكاميرا. اسمح للموقع باستخدام الكاميرا من إعدادات المتصفح ثم أعد المحاولة.'
            : name === 'NotFoundError'
              ? 'لا توجد كاميرا متاحة على هذا الجهاز.'
              : 'تعذر تشغيل الكاميرا.',
        );
      }
    })();

    return () => {
      cancelled = true;
      controls?.stop();
    };
  }, [continuous, onClose]);

  return (
    <Sheet open onClose={onClose} title={title} footer={<Button block size="lg" variant="outline" onClick={onClose}>{continuous ? 'تم' : 'إلغاء'}</Button>}>
      {error ? (
        <div className="flex flex-col items-center gap-3 py-8 text-center">
          <Camera className="size-10 text-muted" />
          <p className="text-[15px] leading-7">{error}</p>
        </div>
      ) : (
        <div className="space-y-3">
          <div className="relative overflow-hidden rounded-2xl bg-black">
            <video ref={videoRef} className="aspect-[4/3] w-full object-cover" muted playsInline />
            {/* Aiming guide */}
            <div className="pointer-events-none absolute inset-x-8 top-1/2 h-24 -translate-y-1/2 rounded-xl border-2 border-white/80" />
          </div>
          <p className="text-center text-sm text-muted">وجّه الكاميرا إلى خطوط الباركود على العلبة</p>
          {last && (
            <p className="text-center text-sm font-semibold text-success">
              تمت قراءة: <span className="ltr inline-block">{last}</span>
            </p>
          )}
        </div>
      )}
    </Sheet>
  );
}

/** Small camera button for fields that accept a barcode. */
export function ScanButton({ onClick, label = 'مسح الباركود بالكاميرا' }: { onClick: () => void; label?: string }) {
  return (
    <IconButton label={label} onClick={onClick} className="shrink-0 border border-line bg-surface">
      <ScanBarcode className="size-5 text-brand-700" />
    </IconButton>
  );
}
