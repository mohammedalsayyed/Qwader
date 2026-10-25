import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { useNavigate } from 'react-router';
import { LogOut } from 'lucide-react';
import { authApi } from '@/api/endpoints';
import { useAuth } from '@/auth/AuthProvider';
import { Button } from '@/components/ui/Button';
import { Field, Input } from '@/components/ui/Field';
import { Card, PageHeader } from '@/components/ui/Feedback';
import { useToast } from '@/components/ui/Toast';
import { applyServerErrors } from '@/lib/forms';

export const passwordSchema = z
  .string()
  .min(8, 'كلمة المرور يجب ألا تقل عن 8 أحرف')
  .regex(/[0-9]/, 'كلمة المرور يجب أن تحتوي على رقم واحد على الأقل')
  .regex(/[^\d\s]/, 'كلمة المرور يجب أن تحتوي على حرف واحد على الأقل');

const schema = z
  .object({
    currentPassword: z.string().min(1, 'كلمة المرور الحالية مطلوبة'),
    newPassword: passwordSchema,
    confirmPassword: z.string(),
  })
  .refine((v) => v.newPassword === v.confirmPassword, { path: ['confirmPassword'], message: 'كلمتا المرور غير متطابقتين' });
type FormValues = z.infer<typeof schema>;

export function ChangePasswordPage() {
  const { user, reload, logout } = useAuth();
  const navigate = useNavigate();
  const toast = useToast();
  const forced = !!user?.mustChangePassword;

  const {
    register,
    handleSubmit,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({ resolver: zodResolver(schema), defaultValues: { currentPassword: '', newPassword: '', confirmPassword: '' } });

  const onSubmit = handleSubmit(async (v) => {
    try {
      await authApi.changePassword(v.currentPassword, v.newPassword);
      await reload(); // replaces the restricted token with a full one
      toast.success('تم تغيير كلمة المرور');
      navigate('/', { replace: true });
    } catch (e) {
      const msg = applyServerErrors(e, setError, ['currentPassword', 'newPassword']);
      if (msg) toast.error(msg);
    }
  });

  const form = (
    <form onSubmit={onSubmit} noValidate className="space-y-4">
      <Field label="كلمة المرور الحالية" required error={errors.currentPassword?.message}>
        {(id, d) => <Input id={id} aria-describedby={d} type="password" autoComplete="current-password" invalid={!!errors.currentPassword} {...register('currentPassword')} />}
      </Field>
      <Field label="كلمة المرور الجديدة" required error={errors.newPassword?.message} hint="8 أحرف على الأقل، وتحتوي على رقم وحرف">
        {(id, d) => <Input id={id} aria-describedby={d} type="password" autoComplete="new-password" invalid={!!errors.newPassword} {...register('newPassword')} />}
      </Field>
      <Field label="تأكيد كلمة المرور الجديدة" required error={errors.confirmPassword?.message}>
        {(id, d) => <Input id={id} aria-describedby={d} type="password" autoComplete="new-password" invalid={!!errors.confirmPassword} {...register('confirmPassword')} />}
      </Field>
      <Button type="submit" block size="lg" loading={isSubmitting}>
        حفظ كلمة المرور
      </Button>
    </form>
  );

  if (forced) {
    return (
      <div className="flex min-h-dvh items-center justify-center bg-canvas px-4 py-10">
        <Card className="w-full max-w-sm p-6">
          <h1 className="text-lg font-bold">تعيين كلمة مرور جديدة</h1>
          <p className="mb-5 mt-1 text-sm text-muted">لحماية حسابك، اختر كلمة مرور خاصة بك قبل البدء.</p>
          {form}
          <Button variant="ghost" block className="mt-2" onClick={() => void logout()}>
            <LogOut className="size-4" /> تسجيل الخروج
          </Button>
        </Card>
      </div>
    );
  }

  return (
    <div className="mx-auto max-w-md">
      <PageHeader title="تغيير كلمة المرور" />
      <Card className="p-5">{form}</Card>
    </div>
  );
}
