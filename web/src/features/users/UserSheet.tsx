import { useState } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { KeyRound } from 'lucide-react';
import { rolesApi, usersApi, type User } from '@/api/endpoints';
import { useAuth } from '@/auth/AuthProvider';
import { Button } from '@/components/ui/Button';
import { Checkbox, Field, Input, Switch } from '@/components/ui/Field';
import { Sheet } from '@/components/ui/Sheet';
import { Spinner } from '@/components/ui/Feedback';
import { useToast } from '@/components/ui/Toast';
import { useConfirm } from '@/components/ui/Confirm';
import { applyServerErrors, blankToNull } from '@/lib/forms';
import { passwordSchema } from '@/features/auth/ChangePasswordPage';

const baseSchema = z.object({
  userName: z.string().trim().min(3, 'اسم المستخدم يجب أن يكون 3 أحرف على الأقل').regex(/^\S+$/, 'اسم المستخدم يجب ألا يحتوي على مسافات'),
  fullName: z.string().trim().min(1, 'الاسم الكامل مطلوب').max(100),
  phoneNumber: z.string().trim().max(20, 'رقم الجوال طويل جدًا').optional(),
  password: z.string().optional(),
  isActive: z.boolean(),
  roles: z.array(z.string()).min(1, 'اختر دورًا واحدًا على الأقل'),
});
type FormValues = z.infer<typeof baseSchema>;

const createSchema = baseSchema.extend({ password: passwordSchema });

export function UserSheet({ user, onClose }: { user: User | null; onClose: () => void }) {
  const isNew = !user;
  const toast = useToast();
  const confirm = useConfirm();
  const queryClient = useQueryClient();
  const { user: me } = useAuth();
  const [resetOpen, setResetOpen] = useState(false);

  const roles = useQuery({ queryKey: ['roles'], queryFn: rolesApi.list });

  const {
    register,
    control,
    handleSubmit,
    setError,
    formState: { errors },
  } = useForm<FormValues>({
    resolver: zodResolver(isNew ? createSchema : baseSchema),
    defaultValues: {
      userName: user?.userName ?? '',
      fullName: user?.fullName ?? '',
      phoneNumber: user?.phoneNumber ?? '',
      password: '',
      isActive: user?.isActive ?? true,
      roles: user?.roles.map((r) => r.name) ?? [],
    },
  });

  const save = useMutation({
    mutationFn: (v: FormValues) =>
      isNew
        ? usersApi.create({ userName: v.userName, fullName: v.fullName, phoneNumber: blankToNull(v.phoneNumber), password: v.password!, roles: v.roles })
        : usersApi.update(user.id, { fullName: v.fullName, phoneNumber: blankToNull(v.phoneNumber), isActive: v.isActive, roles: v.roles }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['users'] });
      void queryClient.invalidateQueries({ queryKey: ['roles'] });
      toast.success(isNew ? 'تمت إضافة المستخدم' : 'تم حفظ التعديلات');
      onClose();
    },
    onError: (e) => {
      const msg = applyServerErrors(e, setError, ['userName', 'fullName', 'phoneNumber', 'password', 'roles']);
      if (msg) toast.error(msg);
    },
  });

  const onSubmit = handleSubmit(async (v) => {
    if (!isNew && user.isActive && !v.isActive) {
      const ok = await confirm({
        title: 'تعطيل المستخدم',
        message: `سيتم تسجيل خروج «${user.fullName}» من جميع الأجهزة ولن يتمكن من الدخول. هل تريد المتابعة؟`,
        confirmLabel: 'تعطيل',
        danger: true,
      });
      if (!ok) return;
    }
    save.mutate(v);
  });

  return (
    <Sheet
      open
      onClose={onClose}
      title={isNew ? 'مستخدم جديد' : 'تعديل المستخدم'}
      footer={
        <Button block size="lg" loading={save.isPending} onClick={() => void onSubmit()}>
          {isNew ? 'إضافة المستخدم' : 'حفظ التعديلات'}
        </Button>
      }
    >
      <form onSubmit={onSubmit} noValidate className="space-y-4">
        <Field label="الاسم الكامل" required error={errors.fullName?.message}>
          {(id, d) => <Input id={id} aria-describedby={d} invalid={!!errors.fullName} {...register('fullName')} />}
        </Field>
        <Field label="اسم المستخدم (للدخول)" required error={errors.userName?.message}>
          {(id, d) => (
            <Input id={id} aria-describedby={d} autoCapitalize="none" disabled={!isNew} invalid={!!errors.userName} className="ltr" {...register('userName')} />
          )}
        </Field>
        <Field label="رقم الجوال" error={errors.phoneNumber?.message}>
          {(id, d) => <Input id={id} aria-describedby={d} type="tel" inputMode="tel" className="ltr" {...register('phoneNumber')} />}
        </Field>
        {isNew && (
          <Field label="كلمة المرور المؤقتة" required error={errors.password?.message} hint="سيُطلب من المستخدم تغييرها عند أول دخول">
            {(id, d) => <Input id={id} aria-describedby={d} type="text" autoComplete="off" className="ltr" invalid={!!errors.password} {...register('password')} />}
          </Field>
        )}

        <fieldset>
          <legend className="mb-1 text-sm font-medium">
            الأدوار <span className="text-danger">*</span>
          </legend>
          {roles.isPending ? (
            <Spinner className="py-4" />
          ) : (
            <Controller
              control={control}
              name="roles"
              render={({ field }) => (
                <div className="grid gap-x-4 sm:grid-cols-2">
                  {roles.data?.map((r) => (
                    <Checkbox
                      key={r.id}
                      label={r.displayName}
                      checked={field.value.includes(r.name)}
                      onChange={(checked) => field.onChange(checked ? [...field.value, r.name] : field.value.filter((n) => n !== r.name))}
                    />
                  ))}
                </div>
              )}
            />
          )}
          {errors.roles && <p className="text-sm text-danger">{errors.roles.message}</p>}
        </fieldset>

        {!isNew && (
          <>
            <Controller
              control={control}
              name="isActive"
              render={({ field }) => (
                <Switch
                  label="الحساب فعّال"
                  description={field.value ? 'يستطيع الدخول إلى النظام' : 'لا يستطيع الدخول إلى النظام'}
                  checked={field.value}
                  onChange={field.onChange}
                />
              )}
            />
            {user.id !== me?.id && (
              <Button variant="outline" block onClick={() => setResetOpen(true)}>
                <KeyRound className="size-4" /> تعيين كلمة مرور جديدة
              </Button>
            )}
          </>
        )}
      </form>

      {resetOpen && user && <ResetPasswordSheet user={user} onClose={() => setResetOpen(false)} />}
    </Sheet>
  );
}

function ResetPasswordSheet({ user, onClose }: { user: User; onClose: () => void }) {
  const toast = useToast();
  const schema = z.object({ newPassword: passwordSchema });
  const {
    register,
    handleSubmit,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<{ newPassword: string }>({ resolver: zodResolver(schema), defaultValues: { newPassword: '' } });

  const onSubmit = handleSubmit(async (v) => {
    try {
      await usersApi.resetPassword(user.id, v.newPassword);
      toast.success('تم تعيين كلمة المرور، وسيُطلب من المستخدم تغييرها عند الدخول');
      onClose();
    } catch (e) {
      const msg = applyServerErrors(e, setError, ['newPassword']);
      if (msg) toast.error(msg);
    }
  });

  return (
    <Sheet
      open
      onClose={onClose}
      title={`كلمة مرور جديدة لـ ${user.fullName}`}
      footer={
        <Button block size="lg" loading={isSubmitting} onClick={() => void onSubmit()}>
          تعيين
        </Button>
      }
    >
      <form onSubmit={onSubmit} noValidate>
        <Field label="كلمة المرور المؤقتة" required error={errors.newPassword?.message} hint="سيتم تسجيل خروجه من جميع الأجهزة">
          {(id, d) => <Input id={id} aria-describedby={d} autoComplete="off" className="ltr" invalid={!!errors.newPassword} {...register('newPassword')} />}
        </Field>
      </form>
    </Sheet>
  );
}
