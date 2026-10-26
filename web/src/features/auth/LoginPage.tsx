import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { Eye, EyeOff } from 'lucide-react';
import { useLocation, useNavigate } from 'react-router';
import { useAuth } from '@/auth/AuthProvider';
import { Button } from '@/components/ui/Button';
import { Field, Input } from '@/components/ui/Field';
import { errorMessage } from '@/lib/forms';
import { Credit } from '@/components/layout/Credit';

const schema = z.object({
  userName: z.string().trim().min(1, 'اسم المستخدم مطلوب'),
  password: z.string().min(1, 'كلمة المرور مطلوبة'),
});
type FormValues = z.infer<typeof schema>;

export function LoginPage() {
  const { login } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const [showPassword, setShowPassword] = useState(false);
  const [serverError, setServerError] = useState<string | null>(null);

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({ resolver: zodResolver(schema), defaultValues: { userName: '', password: '' } });

  const onSubmit = handleSubmit(async (values) => {
    setServerError(null);
    try {
      const user = await login(values.userName, values.password);
      const from = (location.state as { from?: string } | null)?.from;
      navigate(user.mustChangePassword ? '/account/password' : (from ?? '/'), { replace: true });
    } catch (e) {
      setServerError(errorMessage(e));
    }
  });

  return (
    <div className="flex min-h-dvh flex-col items-center justify-center bg-gradient-to-b from-brand-800 to-brand-900 px-4 py-10">
      <div className="mb-6 text-center text-white">
        <img src="/icons/icon-192.png" alt="" className="mx-auto mb-3 size-16 rounded-2xl shadow-lg ring-1 ring-white/20" />
        <h1 className="text-2xl font-bold">معرض قويدر</h1>
        <p className="mt-1 text-brand-100">للأدوات والأجهزة المنزلية</p>
      </div>

      <form onSubmit={onSubmit} noValidate className="w-full max-w-sm space-y-4 rounded-3xl bg-surface p-6 shadow-xl">
        <h2 className="text-lg font-bold">تسجيل الدخول</h2>

        {serverError && (
          <p role="alert" className="rounded-xl bg-red-50 px-4 py-3 text-sm text-danger">
            {serverError}
          </p>
        )}

        <Field label="اسم المستخدم" required error={errors.userName?.message}>
          {(id, d) => (
            <Input id={id} aria-describedby={d} autoComplete="username" autoCapitalize="none" invalid={!!errors.userName} {...register('userName')} />
          )}
        </Field>

        <Field label="كلمة المرور" required error={errors.password?.message}>
          {(id, d) => (
            <div className="relative">
              <Input
                id={id}
                aria-describedby={d}
                type={showPassword ? 'text' : 'password'}
                autoComplete="current-password"
                invalid={!!errors.password}
                className="pe-12"
                {...register('password')}
              />
              <button
                type="button"
                onClick={() => setShowPassword((s) => !s)}
                aria-label={showPassword ? 'إخفاء كلمة المرور' : 'إظهار كلمة المرور'}
                className="absolute end-1 top-1/2 flex size-10 -translate-y-1/2 items-center justify-center rounded-lg text-muted"
              >
                {showPassword ? <EyeOff className="size-5" /> : <Eye className="size-5" />}
              </button>
            </div>
          )}
        </Field>

        <Button type="submit" block size="lg" loading={isSubmitting}>
          دخول
        </Button>
      </form>

      <Credit className="mt-8 text-brand-100" />
    </div>
  );
}
