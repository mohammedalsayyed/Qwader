import { useMutation } from '@tanstack/react-query';
import { DatabaseBackup, Download } from 'lucide-react';
import { settingsApi } from '@/api/endpoints';
import { useAuth } from '@/auth/AuthProvider';
import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/Feedback';
import { useToast } from '@/components/ui/Toast';
import { errorMessage } from '@/lib/forms';

/** Admin-only: download a ZIP of the whole store (data, Excel lists, uploaded files). */
export function BackupCard() {
  const { user } = useAuth();
  const toast = useToast();
  const download = useMutation({
    mutationFn: settingsApi.backup,
    onSuccess: () => toast.success('تم تنزيل النسخة الاحتياطية — احفظها في مكان آمن'),
    onError: (e) => toast.error(errorMessage(e)),
  });
  if (!user?.roles.includes('Admin')) return null;

  return (
    <Card className="space-y-3 p-5">
      <h2 className="flex items-center gap-2 font-bold"><DatabaseBackup className="size-5 text-brand-700" aria-hidden /> النسخ الاحتياطي</h2>
      <p className="text-sm leading-6 text-muted">
        ملف واحد (ZIP) فيه كل بيانات المعرض: المنتجات والأصناف والمخزون والفواتير والعملاء والموردين والحسابات والسندات والإعدادات،
        مع نسخة Excel من القوائم الرئيسية وصور المنتجات والمرفقات. لا يحتوي على كلمات المرور.
      </p>
      <p className="rounded-xl bg-amber-50 p-3 text-xs leading-5 text-amber-800">
        يُنصح بتنزيل نسخة كل أسبوع على الأقل وقبل أي «تصفير»، وحفظها على جهاز آخر أو في Google Drive. فيها بيانات العملاء، فلا ترسلها لأحد.
      </p>
      <Button loading={download.isPending} onClick={() => download.mutate()}>
        <Download className="size-4" aria-hidden /> تنزيل نسخة احتياطية الآن
      </Button>
    </Card>
  );
}
