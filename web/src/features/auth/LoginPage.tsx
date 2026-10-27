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
    <div className="flex min-h-dvh flex-col items-center justify-center bg-gradient-to-b from-white to-canvas px-4 py-10">
      {/* The logo carries the store's name and line of business. */}
      <h1 className="mb-6">
        <img src="/brand/logo.png" alt="معرض قويدر للأدوات والأجهزة المنزلية" className="mx-auto h-auto w-52 sm:w-60" width={640} height={616} />
      </h1>

      <form onSubmit={onSubmit} noValidate className="w-full max-w-sm space-y-4 rounded-3xl border border-line bg-surface p-6 shadow-xl shadow-brand-900/10">
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

      <Credit className="mt-8 text-muted" />
    </div>
  );
}
