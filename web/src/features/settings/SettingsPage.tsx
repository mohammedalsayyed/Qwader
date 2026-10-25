import { useEffect } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { settingsApi, type Settings } from '@/api/endpoints';
import { Button } from '@/components/ui/Button';
import { Field, Input, TextArea } from '@/components/ui/Field';
import { Card, ErrorState, PageHeader, Spinner } from '@/components/ui/Feedback';
import { useToast } from '@/components/ui/Toast';
import { applyServerErrors, blankToNull, errorMessage } from '@/lib/forms';
import { toAsciiDigits } from '@/lib/arabic';

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

  if (settings.isPending) return <Spinner />;
  if (settings.isError) return <ErrorState message={errorMessage(settings.error)} onRetry={() => void settings.refetch()} />;

  const onSubmit = handleSubmit((v) =>
    save.mutate({ ...v, storeAddress: blankToNull(v.storeAddress), storePhone: blankToNull(v.storePhone), invoiceFooter: blankToNull(v.invoiceFooter) }),
  );

  return (
    <form onSubmit={onSubmit} noValidate className="mx-auto max-w-2xl space-y-4">
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
          <Field label="حد التنبيه الافتراضي للمخزون" error={errors.defaultReorderLevel?.message}>
            {(id, d) => <Input id={id} aria-describedby={d} inputMode="decimal" className="ltr" invalid={!!errors.defaultReorderLevel} {...register('defaultReorderLevel')} />}
          </Field>
        </div>
      </Card>

      <div className="pb-safe sticky bottom-20 z-20 lg:bottom-4">
        <Button type="submit" block size="lg" loading={save.isPending} disabled={!isDirty}>
          حفظ الإعدادات
        </Button>
      </div>
    </form>
  );
}
