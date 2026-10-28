import { useEffect } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { settingsApi, type Settings } from '@/api/endpoints';
import { Button } from '@/components/ui/Button';
import { Field, Input, TextArea } from '@/components/ui/Field';
import { Card, ErrorState, PageHeader, Spinner } from '@/components/ui/Feedback';
import { useConfirm } from '@/components/ui/Confirm';
import { useToast } from '@/components/ui/Toast';
import { applyServerErrors, blankToNull, errorMessage } from '@/lib/forms';
import { toAsciiDigits } from '@/lib/arabic';
import { cn } from '@/lib/cn';
import { BackupCard } from './BackupCard';
import { SystemResetCard } from './SystemResetCard';

const num = (min: number, max: number, message: string) =>
  z.preprocess((v) => (typeof v === 'string' ? Number(toAsciiDigits(v.trim()) || NaN) : v), z.number({ invalid_type_error: 'أدخل رقمًا صحيحًا' }).min(min, message).max(max, message));

const schema = z.object({
  storeName: z.string().trim().min(1, 'اسم المعرض مطلوب').max(100),
  storeAddress: z.string().trim().max(200).nullable(),
  storePhone: z.string().trim().max(30).nullable(),
  invoiceFooter: z.string().trim().max(300).nullable(),
  defaultMarginPercent: num(0, 1000, 'نسبة الربح يجب أن تكون بين 0 و1000'),
  maxDiscountPercent: num(0, 100, 'نسبة الخصم يجب أن تكون بين 0 و100'),
  defaultReorderLevel: num(0, 1_000_000, 'حد التنبيه لا يمكن أن يكون سالبًا'),
  saleTransfersPending: z.boolean(),
});
type FormInput = z.input<typeof schema>;
type FormValues = z.output<typeof schema>;

const FIELDS = ['storeName', 'storeAddress', 'storePhone', 'invoiceFooter', 'defaultMarginPercent', 'maxDiscountPercent', 'defaultReorderLevel'] as const;

export function SettingsPage() {
  const toast = useToast();
  const queryClient = useQueryClient();
  const settings = useQuery({ queryKey: ['settings'], queryFn: settingsApi.get });

  const {
    register,
    handleSubmit,
    reset,
    setError,
    control,
    formState: { errors, isDirty },
  } = useForm<FormInput, unknown, FormValues>({ resolver: zodResolver(schema) });

  useEffect(() => {
    if (settings.data) reset(settings.data);
  }, [settings.data, reset]);

  const save = useMutation({
    mutationFn: (v: Settings) => settingsApi.update(v),
    onSuccess: (data) => {
      queryClient.setQueryData(['settings'], data);
      reset(data);
      toast.success('تم حفظ الإعدادات');
    },
    onError: (e) => {
      const msg = applyServerErrors(e, setError, FIELDS);
      if (msg) toast.error(msg);
    },
  });

  const confirm = useConfirm();
  const applyReorder = useMutation({
    mutationFn: settingsApi.applyReorderLevel,
    onSuccess: (r) => {
      toast.success(`تم تطبيق حد التنبيه على ${r.changed} منتج`);
      // Low-stock counts on the dashboard, products and the bell all change.
      for (const k of ['products', 'product', 'inventory-summary', 'notifications']) void queryClient.invalidateQueries({ queryKey: [k] });
    },
    onError: (e) => toast.error(errorMessage(e)),
  });

  if (settings.isPending) return <Spinner />;
  if (settings.isError) return <ErrorState message={errorMessage(settings.error)} onRetry={() => void settings.refetch()} />;

  const onSubmit = handleSubmit((v) =>
    save.mutate({ ...v, storeAddress: blankToNull(v.storeAddress), storePhone: blankToNull(v.storePhone), invoiceFooter: blankToNull(v.invoiceFooter) }),
  );

  return (
    <div className="mx-auto max-w-2xl space-y-4">
      <form onSubmit={onSubmit} noValidate className="space-y-4">
        <PageHeader title="الإعدادات" />

        <Card className="space-y-4 p-5">
          <h2 className="font-bold">بيانات المعرض</h2>
          <p className="-mt-3 text-sm text-muted">تظهر في رأس الفواتير والسندات المطبوعة.</p>
          <Field label="اسم المعرض" required error={errors.storeName?.message}>
            {(id, d) => <Input id={id} aria-describedby={d} invalid={!!errors.storeName} {...register('storeName')} />}
          </Field>
          <Field label="العنوان" error={errors.storeAddress?.message}>
            {(id, d) => <Input id={id} aria-describedby={d} {...register('storeAddress')} />}
          </Field>
          <Field label="رقم الهاتف" error={errors.storePhone?.message}>
            {(id, d) => <Input id={id} aria-describedby={d} type="tel" inputMode="tel" className="ltr" {...register('storePhone')} />}
          </Field>
          <Field label="نص أسفل الفاتورة" error={errors.invoiceFooter?.message}>
            {(id, d) => <TextArea id={id} aria-describedby={d} rows={2} {...register('invoiceFooter')} />}
          </Field>
        </Card>

        <Card className="space-y-4 p-5">
          <h2 className="font-bold">البيع والتسعير</h2>
          <div className="grid gap-4 sm:grid-cols-3">
            <Field label="نسبة الربح المقترحة %" error={errors.defaultMarginPercent?.message} hint="لاقتراح سعر البيع من التكلفة">
              {(id, d) => <Input id={id} aria-describedby={d} inputMode="decimal" className="ltr" invalid={!!errors.defaultMarginPercent} {...register('defaultMarginPercent')} />}
            </Field>
            <Field label="أقصى خصم مسموح %" error={errors.maxDiscountPercent?.message}>
              {(id, d) => <Input id={id} aria-describedby={d} inputMode="decimal" className="ltr" invalid={!!errors.maxDiscountPercent} {...register('maxDiscountPercent')} />}
            </Field>
            <Field label="حد التنبيه الافتراضي للمخزون" error={errors.defaultReorderLevel?.message} hint="يُعبّأ تلقائيًا للمنتجات الجديدة">
              {(id, d) => <Input id={id} aria-describedby={d} inputMode="decimal" className="ltr" invalid={!!errors.defaultReorderLevel} {...register('defaultReorderLevel')} />}
            </Field>
          </div>
          {/* Each product keeps its own limit; this is the way to bring all of them to the default. */}
          <div className="flex flex-wrap items-center justify-between gap-2 rounded-xl bg-canvas p-3">
            <p className="text-xs leading-5 text-muted">
              كل منتج له حد تنبيه خاص به. لجعل حد كل المنتجات الحالية = {settings.data.defaultReorderLevel}، احفظ الإعدادات ثم اضغط:
            </p>
            <Button variant="outline" size="sm" disabled={isDirty} loading={applyReorder.isPending}
              onClick={async () => {
                if (await confirm({
                  title: 'تطبيق حد التنبيه على كل المنتجات',
                  message: `سيصبح حد التنبيه لكل المنتجات الحالية ${settings.data.defaultReorderLevel}، وتُستبدل الحدود الخاصة التي وضعتها لبعض المنتجات.`,
                  confirmLabel: 'تطبيق على الكل',
                })) applyReorder.mutate();
              }}>
              تطبيق على كل المنتجات
            </Button>
          </div>
        </Card>

        <Card className="space-y-2 p-5">
          <h2 className="font-bold">الحوالات عند البيع</h2>
          <p className="text-sm text-muted">عندما يدفع الزبون في نقطة البيع بحوالة (جوال بي، بنك…) تُسجَّل الحوالة تلقائيًا من بيانات الفاتورة.</p>
          <Controller control={control} name="saleTransfersPending"
            render={({ field }) => (
              <div className="grid gap-2 sm:grid-cols-2" role="radiogroup" aria-label="حالة الحوالة عند البيع">
                {[true, false].map((v) => (
                  <button key={String(v)} type="button" role="radio" aria-checked={field.value === v} onClick={() => field.onChange(v)}
                    className={cn('rounded-xl border p-3 text-start', field.value === v ? 'border-brand-700 bg-brand-50' : 'border-line')}>
                    <span className="block font-semibold">{v ? 'بانتظار التحقق' : 'مؤكدة مباشرة'}</span>
                    <span className="block text-xs leading-5 text-muted">
                      {v ? 'تبقى دينًا على العميل حتى يؤكدها المحاسب بعد رؤيتها في الحساب (الأكثر أمانًا).' : 'تُحسب مدفوعة فورًا عند البيع، وتظهر في قائمة الحوالات كمؤكدة.'}
                    </span>
                  </button>
                ))}
              </div>
            )} />
        </Card>

        <div className="sticky bottom-nav z-20 lg:bottom-4">
          <Button type="submit" block size="lg" loading={save.isPending} disabled={!isDirty}>
            حفظ الإعدادات
          </Button>
        </div>
      </form>

      <BackupCard />
      <SystemResetCard />
    </div>
  );
}
