import { useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowRight, Ban, Banknote, HandCoins, MinusCircle, Pencil, Phone, PlusCircle, Receipt, Trash2, UserPlus, Wallet } from 'lucide-react';
import { useConfirm } from '@/components/ui/Confirm';
import { ENTRY_TYPE, PAY_PERIOD, payrollApi, type Employee, type PayPeriod, type PayrollEntry, type PayrollEntryType, type PayrollSettlement } from '@/api/payroll';
import { Button } from '@/components/ui/Button';
import { Field, Input, Select, Switch, TextArea } from '@/components/ui/Field';
import { Badge, Card, EmptyState, ErrorState, PageHeader, Spinner } from '@/components/ui/Feedback';
import { Stat } from '@/components/ui/Layout';
import { Sheet } from '@/components/ui/Sheet';
import { useToast } from '@/components/ui/Toast';
import { AccountChips } from '@/components/finance/FinanceBits';
import { cn } from '@/lib/cn';
import { errorMessage } from '@/lib/forms';
import { formatDate, formatDateTime, formatMoney, localToday } from '@/lib/format';
import { useIdempotencyKey } from '@/lib/hooks';
import { parseNumber, round2 } from '@/lib/numbers';
import { useLookups, useStoreSettings } from '@/lib/queries';

// ---------- period arithmetic on local dates (YYYY-MM-DD) ----------

const toUtc = (d: string) => new Date(`${d}T00:00:00Z`);
const iso = (d: Date) => d.toISOString().slice(0, 10);
const addDays = (d: string, n: number) => { const t = toUtc(d); t.setUTCDate(t.getUTCDate() + n); return iso(t); };
const daysBetween = (from: string, to: string) => Math.round((toUtc(to).getTime() - toUtc(from).getTime()) / 86_400_000) + 1;
const monthEnd = (d: string) => { const t = toUtc(d); return iso(new Date(Date.UTC(t.getUTCFullYear(), t.getUTCMonth() + 1, 0))); };

/** The next unpaid period: right after the last settlement (or from the start date), one period long. */
function nextPeriod(e: Employee) {
  const today = localToday();
  const from = e.lastPaidTo ? addDays(e.lastPaidTo, 1) : e.startDate ?? (e.payPeriod === 'Monthly' ? `${today.slice(0, 8)}01` : today);
  const to = e.payPeriod === 'Daily' ? (from > today ? from : today) : e.payPeriod === 'Weekly' ? addDays(from, 6) : monthEnd(from);
  return { from, to };
}

/** Days, weeks or months between two dates; whole calendar months count as 1 each. */
export function payUnits(period: PayPeriod, from: string, to: string): number {
  if (to < from) return 0;
  const days = daysBetween(from, to);
  if (period === 'Daily') return days;
  if (period === 'Weekly') return round2(days / 7);
  const f = toUtc(from);
  const t = toUtc(to);
  if (f.getUTCDate() === 1 && to === monthEnd(to)) return (t.getUTCFullYear() - f.getUTCFullYear()) * 12 + t.getUTCMonth() - f.getUTCMonth() + 1;
  return round2(days / 30);
}

/** The most units a period can hold, as the server checks it (a month counts as at least 28 days). */
export function maxUnits(period: PayPeriod, from: string, to: string): number {
  if (to < from) return 0;
  const days = daysBetween(from, to);
  return period === 'Daily' ? days : Math.ceil((days / (period === 'Weekly' ? 7 : 28)) * 100) / 100;
}

const rateLabel = (period: PayPeriod, rate: number) => `${formatMoney(rate)} / ${PAY_PERIOD[period].unit}`;

// ---------- list ----------

export function PayrollPage() {
  const [adding, setAdding] = useState(false);
  const query = useQuery({ queryKey: ['payroll'], queryFn: payrollApi.list });

  return (
    <>
      <PageHeader title="الرواتب" subtitle="العمال ورواتبهم وسلفهم ومخالصاتهم"
        actions={<Button onClick={() => setAdding(true)}><UserPlus className="size-4" /> إضافة عامل</Button>} />
      {query.isPending ? <Spinner /> : query.isError ? <ErrorState message={errorMessage(query.error)} onRetry={() => void query.refetch()} />
        : query.data.length === 0 ? (
          <EmptyState icon={<Banknote className="size-10" />} title="لا يوجد عمال في الرواتب بعد"
            description="العامل مستخدم في النظام: أضفه أولًا من «المستخدمون والصلاحيات»، ثم سجّل راتبه هنا." />
        ) : (
          <ul className="grid grid-cols-1 gap-2 md:grid-cols-2">
            {query.data.map((e) => (
              <li key={e.id}>
                <Link to={`/payroll/${e.id}`} className="block rounded-card border border-line bg-surface p-4 hover:border-brand-200">
                  <div className="flex items-start justify-between gap-2">
                    <div className="min-w-0">
                      <p className="flex items-center gap-2 font-semibold"><span className="truncate">{e.fullName}</span>{!e.isActive && <Badge tone="danger">متوقف</Badge>}</p>
                      <p className="ltr text-start text-sm text-muted">{e.phone ?? '—'}</p>
                    </div>
                    <span className="shrink-0 text-end">
                      <span className="tabular block font-bold">{formatMoney(e.rate)}</span>
                      <span className="block text-xs text-muted">{PAY_PERIOD[e.payPeriod].label}</span>
                    </span>
                  </div>
                  <div className="mt-2 flex flex-wrap gap-1.5 text-xs">
                    {e.openAdvances > 0 && <Badge tone="warning">سلف {formatMoney(e.openAdvances)}</Badge>}
                    {e.openDeductions > 0 && <Badge tone="danger">خصم {formatMoney(e.openDeductions)}</Badge>}
                    {e.openBonuses > 0 && <Badge tone="success">مكافأة {formatMoney(e.openBonuses)}</Badge>}
                    <span className="text-muted">{e.lastPaidTo ? `آخر مخالصة حتى ${formatDate(e.lastPaidTo)}` : 'لا توجد مخالصات بعد'}</span>
                  </div>
                </Link>
              </li>
            ))}
          </ul>
        )}
      {adding && <EmployeeSheet employee={null} onClose={() => setAdding(false)} />}
    </>
  );
}

// ---------- one employee (managers) ----------

type Action = PayrollEntryType | 'settle' | 'edit';

export function EmployeePayrollPage() {
  const id = Number(useParams().id);
  const navigate = useNavigate();
  const [action, setAction] = useState<Action | null>(null);
  const query = useQuery({ queryKey: ['payroll', id], queryFn: () => payrollApi.get(id) });

  if (query.isPending) return <Spinner />;
  if (query.isError) return <ErrorState message={errorMessage(query.error)} onRetry={() => void query.refetch()} />;
  const e = query.data;

  return (
    <div className="mx-auto max-w-3xl space-y-4">
      <button type="button" onClick={() => navigate('/payroll')} className="flex h-10 items-center gap-1 text-sm text-muted">
        <ArrowRight className="size-4" /> الرواتب
      </button>
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <h1 className="flex items-center gap-2 text-xl font-bold">{e.fullName}{!e.isActive && <Badge tone="danger">متوقف</Badge>}</h1>
          <p className="text-sm text-muted">{rateLabel(e.payPeriod, e.rate)} · راتب {PAY_PERIOD[e.payPeriod].label}</p>
        </div>
        <div className="flex shrink-0 gap-2">
          {e.phone && <a href={`tel:${e.phone}`}><Button variant="outline" aria-label="اتصال"><Phone className="size-4" /></Button></a>}
          <Button variant="outline" onClick={() => setAction('edit')}><Pencil className="size-4" /> تعديل</Button>
        </div>
      </div>

      <Summary employee={e} />

      <div className="grid grid-cols-2 gap-2 sm:grid-cols-4">
        <Button variant="secondary" onClick={() => setAction('Advance')}><HandCoins className="size-4" /> سلفة</Button>
        <Button variant="secondary" onClick={() => setAction('Deduction')}><MinusCircle className="size-4" /> خصم</Button>
        <Button variant="secondary" onClick={() => setAction('Bonus')}><PlusCircle className="size-4" /> مكافأة</Button>
        <Button variant="accent" onClick={() => setAction('settle')}><Wallet className="size-4" /> مخالصة وصرف الراتب</Button>
      </div>

      <History employee={e} manage />

      {action === 'edit' && <EmployeeSheet employee={e} onClose={() => setAction(null)} />}
      {(action === 'Advance' || action === 'Deduction' || action === 'Bonus') && <EntrySheet employee={e} type={action} onClose={() => setAction(null)} />}
      {action === 'settle' && <SettleSheet employee={e} onClose={() => setAction(null)} />}
    </div>
  );
}

// ---------- the signed-in user's own record ----------

export function MyPayrollPage() {
  const query = useQuery({ queryKey: ['payroll', 'me'], queryFn: payrollApi.mine });
  if (query.isPending) return <Spinner />;
  if (query.isError) return <ErrorState message={errorMessage(query.error)} onRetry={() => void query.refetch()} />;
  const e = query.data;
  if (!e) return <EmptyState icon={<Banknote className="size-10" />} title="لا يوجد لك سجل رواتب" description="يسجّل مدير النظام راتبك من شاشة «الرواتب»." />;
  return (
    <div className="mx-auto max-w-3xl space-y-4">
      <PageHeader title="بياناتي المالية" subtitle={`${rateLabel(e.payPeriod, e.rate)} · راتب ${PAY_PERIOD[e.payPeriod].label}`} />
      <Summary employee={e} />
      <History employee={e} manage={false} />
    </div>
  );
}

/** Pay for one period and what is waiting for the next settlement. */
function Summary({ employee: e }: { employee: Employee }) {
  return (
    <div className="grid grid-cols-2 gap-2 lg:grid-cols-4">
      <Stat label={`الراتب لكل ${PAY_PERIOD[e.payPeriod].unit}`} value={formatMoney(e.rate)} />
      <Stat label="سلف لم تُخصم" value={formatMoney(e.openAdvances)} tone={e.openAdvances > 0 ? 'warning' : 'default'} />
      <Stat label="خصومات قادمة" value={formatMoney(e.openDeductions)} tone={e.openDeductions > 0 ? 'danger' : 'default'} />
      <Stat label="مكافآت قادمة" value={formatMoney(e.openBonuses)} tone={e.openBonuses > 0 ? 'success' : 'default'}
        hint={e.lastPaidTo ? `آخر مخالصة حتى ${formatDate(e.lastPaidTo)}` : undefined} />
    </div>
  );
}

function History({ employee: e, manage }: { employee: Employee; manage: boolean }) {
  const [voiding, setVoiding] = useState<{ kind: 'entry'; item: PayrollEntry } | { kind: 'settlement'; item: PayrollSettlement } | null>(null);
  const open = e.entries.filter((x) => x.status === 'Posted' && x.settlementId == null);

  return (
    <>
      <section>
        <h2 className="mb-2 font-bold">بانتظار المخالصة القادمة</h2>
        {open.length === 0 ? <p className="rounded-card border border-dashed border-line p-4 text-center text-sm text-muted">لا توجد سلف أو خصومات أو مكافآت مفتوحة</p> : (
          <Card className="overflow-hidden"><ul className="divide-y divide-line">
            {open.map((x) => <EntryRow key={x.id} entry={x} onVoid={manage && !x.carried ? () => setVoiding({ kind: 'entry', item: x }) : undefined} />)}
          </ul></Card>
        )}
      </section>

      <section>
        <h2 className="mb-2 font-bold">المخالصات</h2>
        {e.settlements.length === 0 ? <p className="rounded-card border border-dashed border-line p-4 text-center text-sm text-muted">لا توجد مخالصات بعد</p> : (
          <ul className="space-y-2">
            {e.settlements.map((s) => (
              <li key={s.id} className={cn('rounded-card border border-line bg-surface p-4', s.status === 'Voided' && 'opacity-60')}>
                <div className="flex items-start justify-between gap-2">
                  <div className="min-w-0">
                    <p className="font-semibold">{formatDate(s.periodFrom)} — {formatDate(s.periodTo)}</p>
                    <p className="text-xs text-muted"><span className="ltr inline-block">{s.number}</span> · {formatDateTime(s.date)}{s.accountName && ` · من ${s.accountName}`}</p>
                  </div>
                  <span className="shrink-0 text-end">
                    <span className="tabular block text-lg font-bold">{formatMoney(s.net)}</span>
                    {s.status === 'Voided' ? <Badge tone="danger">ملغاة</Badge> : <span className="block text-xs text-muted">الصافي المصروف</span>}
                  </span>
                </div>
                <dl className="mt-2 grid grid-cols-1 gap-x-6 gap-y-0.5 text-sm sm:grid-cols-2">
                  <Line label={`${s.units} ${PAY_PERIOD[s.payPeriod].unit} × ${formatMoney(s.rate)}`} value={s.gross} />
                  {s.bonuses > 0 && <Line label="+ مكافآت" value={s.bonuses} />}
                  {s.deductions > 0 && <Line label="− خصومات" value={s.deductions} />}
                  {s.advances > 0 && <Line label="− سلف" value={s.advances} />}
                  {s.carriedAdvance > 0 && <Line label="باقي سلف للمخالصة القادمة" value={s.carriedAdvance} />}
                </dl>
                {s.notes && <p className="mt-1 text-sm text-muted">{s.notes}</p>}
                {s.status === 'Voided' && s.voidReason && <p className="mt-1 text-xs text-danger">سبب الإلغاء: {s.voidReason}</p>}
                {manage && s.status === 'Posted' && (
                  <Button size="sm" variant="ghost" className="mt-2 text-danger" onClick={() => setVoiding({ kind: 'settlement', item: s })}><Ban className="size-4" /> إلغاء المخالصة</Button>
                )}
              </li>
            ))}
          </ul>
        )}
      </section>

      {e.entries.some((x) => x.settlementId != null || x.status === 'Voided') && (
        <section>
          <h2 className="mb-2 font-bold">السجل</h2>
          <Card className="overflow-hidden"><ul className="divide-y divide-line">
            {e.entries.filter((x) => x.settlementId != null || x.status === 'Voided').map((x) => <EntryRow key={x.id} entry={x} />)}
          </ul></Card>
        </section>
      )}

      {voiding && <VoidSheet target={voiding} employeeId={e.id} onClose={() => setVoiding(null)} />}
    </>
  );
}

function Line({ label, value }: { label: string; value: number }) {
  return <div className="flex justify-between gap-2"><dt className="text-muted">{label}</dt><dd className="tabular">{formatMoney(value)}</dd></div>;
}

function EntryRow({ entry: x, onVoid }: { entry: PayrollEntry; onVoid?: () => void }) {
  return (
    <li className={cn('flex items-center gap-3 px-4 py-3', x.status === 'Voided' && 'opacity-60')}>
      <Badge tone={ENTRY_TYPE[x.type].tone}>{ENTRY_TYPE[x.type].label}</Badge>
      <span className="min-w-0 flex-1">
        <span className="block truncate text-sm">{x.notes ?? (x.accountName ? `صُرفت من ${x.accountName}` : '—')}</span>
        <span className="block text-xs text-muted">
          {formatDate(x.date)}{x.accountName && x.notes && ` · من ${x.accountName}`}
          {x.settlementNumber && ` · في المخالصة ${x.settlementNumber}`}
          {x.status === 'Voided' && ` · ملغاة${x.voidReason ? `: ${x.voidReason}` : ''}`}
        </span>
      </span>
      <span className="tabular shrink-0 font-bold">{formatMoney(x.amount)}</span>
      {onVoid && <Button size="sm" variant="ghost" aria-label="إلغاء" onClick={onVoid}><Ban className="size-4 text-danger" /></Button>}
    </li>
  );
}

// ---------- sheets ----------

function useRefresh(employeeId?: number) {
  const queryClient = useQueryClient();
  return (data?: Employee) => {
    if (data) queryClient.setQueryData(['payroll', data.id], data);
    for (const k of ['payroll', 'accounts', 'expenses', 'report']) void queryClient.invalidateQueries({ queryKey: [k], refetchType: k === 'payroll' && employeeId ? 'none' : 'active' });
  };
}

function EmployeeSheet({ employee, onClose }: { employee: Employee | null; onClose: () => void }) {
  const toast = useToast();
  const navigate = useNavigate();
  const refresh = useRefresh();
  const settings = useStoreSettings();
  const candidates = useQuery({ queryKey: ['payroll', 'candidates'], queryFn: payrollApi.candidates, enabled: !employee });
  const [userId, setUserId] = useState(employee ? String(employee.userId) : '');
  const [phone, setPhone] = useState(employee?.phone ?? '');
  const [period, setPeriod] = useState<PayPeriod | null>(employee?.payPeriod ?? null);
  const [rate, setRate] = useState(employee ? String(employee.rate) : '');
  const [startDate, setStartDate] = useState(employee?.startDate ?? '');
  const [notes, setNotes] = useState(employee?.notes ?? '');
  const [isActive, setIsActive] = useState(employee?.isActive ?? true);
  const [error, setError] = useState<string>();
  const payPeriod = period ?? settings.data?.defaultPayPeriod ?? 'Monthly';
  const confirm = useConfirm();
  const queryClient = useQueryClient();
  const remove = useMutation({
    mutationFn: () => payrollApi.remove(employee!.id),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['payroll'] });
      toast.success('حُذف العامل من الرواتب');
      onClose();
      navigate('/payroll');
    },
    onError: (e) => setError(errorMessage(e)),
  });
  const dirty = !employee ? !!(userId || rate || phone || notes) : rate !== String(employee.rate) || phone !== (employee.phone ?? '') || payPeriod !== employee.payPeriod || notes !== (employee.notes ?? '') || isActive !== employee.isActive;

  const save = useMutation({
    mutationFn: () => {
      const input = { userId: Number(userId), phone: phone.trim() || null, payPeriod, rate: parseNumber(rate)!, startDate: startDate || null, notes: notes.trim() || null, isActive };
      return employee ? payrollApi.update(employee.id, input) : payrollApi.create(input);
    },
    onSuccess: (saved) => {
      refresh(saved);
      toast.success(employee ? 'تم حفظ بيانات العامل' : 'تمت إضافة العامل للرواتب');
      onClose();
      if (!employee) navigate(`/payroll/${saved.id}`);
    },
    onError: (e) => setError(errorMessage(e)),
  });

  const submit = () => {
    const r = parseNumber(rate);
    if (!userId) return setError('اختر المستخدم');
    if (r === undefined || Number.isNaN(r) || r < 0) return setError('أدخل الراتب');
    setError(undefined);
    save.mutate();
  };

  return (
    <Sheet open onClose={onClose} dirty={dirty} title={employee ? `تعديل ${employee.fullName}` : 'إضافة عامل للرواتب'}
      footer={<Button block size="lg" loading={save.isPending} onClick={submit}>حفظ</Button>}>
      <div className="space-y-4">
        {error && <p role="alert" className="rounded-xl bg-red-50 p-3 text-sm text-danger">{error}</p>}
        {!employee && (
          <Field label="المستخدم" required hint="العامل مستخدم في النظام؛ أضفه أولًا من «المستخدمون والصلاحيات» إن لم يكن موجودًا">
            {(id, d) => (
              <Select id={id} aria-describedby={d} value={userId} onChange={(e) => {
                setUserId(e.target.value);
                const c = candidates.data?.find((x) => String(x.userId) === e.target.value);
                if (c?.phone && !phone) setPhone(c.phone);
              }}>
                <option value="">{candidates.isPending ? 'جارٍ التحميل…' : 'اختر…'}</option>
                {candidates.data?.map((c) => <option key={c.userId} value={c.userId}>{c.fullName} ({c.userName})</option>)}
              </Select>
            )}
          </Field>
        )}
        <Field label="رقم الجوال" hint="هو جوال حساب المستخدم نفسه: تعديله هنا يعدّله في «المستخدمون» أيضًا">
          {(id, d) => <Input id={id} aria-describedby={d} type="tel" inputMode="tel" className="ltr" value={phone} onChange={(e) => setPhone(e.target.value)} />}
        </Field>
        <fieldset>
          <legend className="mb-2 text-sm font-medium">نوع الراتب <span className="text-danger">*</span></legend>
          <div className="grid grid-cols-3 gap-2" role="radiogroup" aria-label="نوع الراتب">
            {(Object.keys(PAY_PERIOD) as PayPeriod[]).map((p) => (
              <button key={p} type="button" role="radio" aria-checked={payPeriod === p} onClick={() => setPeriod(p)}
                className={cn('h-11 rounded-xl border text-sm font-semibold', payPeriod === p ? 'border-brand-700 bg-brand-50 text-brand-800' : 'border-line')}>
                {PAY_PERIOD[p].label}
              </button>
            ))}
          </div>
        </fieldset>
        <Field label={`الراتب لكل ${PAY_PERIOD[payPeriod].unit} (₪)`} required>
          {(id) => <Input id={id} inputMode="decimal" className="ltr text-lg" value={rate} onChange={(e) => setRate(e.target.value)} />}
        </Field>
        <Field label="تاريخ بدء العمل" hint="تبدأ منه أول مخالصة">{(id) => <Input id={id} type="date" value={startDate} onChange={(e) => setStartDate(e.target.value)} />}</Field>
        <Field label="ملاحظات">{(id) => <TextArea id={id} rows={2} value={notes} onChange={(e) => setNotes(e.target.value)} />}</Field>
        {employee && <Switch label="على رأس العمل" checked={isActive} onChange={setIsActive} />}
        {employee && employee.entries.length === 0 && employee.settlements.length === 0 && (
          <Button variant="ghost" className="text-danger" loading={remove.isPending}
            onClick={async () => { if (await confirm({ title: 'حذف من الرواتب', message: `يُحذف «${employee.fullName}» من الرواتب. حسابه كمستخدم يبقى كما هو.`, confirmLabel: 'حذف', danger: true })) remove.mutate(); }}>
            <Trash2 className="size-4" /> حذف من الرواتب
          </Button>
        )}
      </div>
    </Sheet>
  );
}

function EntrySheet({ employee, type, onClose }: { employee: Employee; type: PayrollEntryType; onClose: () => void }) {
  const toast = useToast();
  const refresh = useRefresh(employee.id);
  const accounts = useLookups().data?.accounts ?? [];
  const [key] = useIdempotencyKey();
  const [amount, setAmount] = useState('');
  const [accountId, setAccountId] = useState<number | null>(null);
  const [date, setDate] = useState(localToday());
  const [notes, setNotes] = useState('');
  const [error, setError] = useState<string>();
  const advance = type === 'Advance';

  const save = useMutation({
    mutationFn: () => payrollApi.addEntry(employee.id, { idempotencyKey: key, type, amount: parseNumber(amount)!, date, financialAccountId: advance ? accountId : null, notes: notes.trim() || null }),
    onSuccess: (data) => {
      refresh(data);
      toast.success(advance ? `تم صرف السلفة ${formatMoney(parseNumber(amount)!)}` : `تم تسجيل ال${ENTRY_TYPE[type].label}`);
      onClose();
    },
    onError: (e) => setError(errorMessage(e)),
  });

  const submit = () => {
    const n = parseNumber(amount);
    if (!n || Number.isNaN(n) || n <= 0) return setError('أدخل مبلغًا صحيحًا');
    if (advance && !accountId) return setError('اختر الحساب الذي تُصرف منه السلفة');
    setError(undefined);
    save.mutate();
  };

  const hint = advance ? 'تُصرف الآن من الحساب الذي تختاره، وتبقى دينًا على العامل حتى تُخصم من مخالصته القادمة (وتُحسب مصروف رواتب عندها).'
    : type === 'Deduction' ? 'يُطرح من المخالصة القادمة، ولا يتحرك أي مبلغ الآن.' : 'تُضاف للمخالصة القادمة، ولا يتحرك أي مبلغ الآن.';
  return (
    <Sheet open onClose={onClose} dirty={!!(amount || notes)} title={`${ENTRY_TYPE[type].label} — ${employee.fullName}`}
      footer={<Button block size="lg" loading={save.isPending} onClick={submit}>{advance ? 'صرف السلفة' : 'حفظ'}</Button>}>
      <div className="space-y-4">
        <p className="rounded-xl bg-black/5 p-3 text-sm">{hint}</p>
        {error && <p role="alert" className="rounded-xl bg-red-50 p-3 text-sm text-danger">{error}</p>}
        <Field label="المبلغ (₪)" required>{(id) => <Input id={id} inputMode="decimal" className="ltr text-lg" value={amount} onChange={(e) => { setAmount(e.target.value); setError(undefined); }} />}</Field>
        {advance && <AccountChips accounts={accounts} value={accountId} onChange={(id) => { setAccountId(id); setError(undefined); }} label="تُصرف من حساب" />}
        <Field label="التاريخ">{(id) => <Input id={id} type="date" max={localToday()} value={date} onChange={(e) => setDate(e.target.value)} />}</Field>
        <Field label={type === 'Deduction' ? 'السبب' : 'ملاحظات'}>{(id) => <TextArea id={id} rows={2} value={notes} onChange={(e) => setNotes(e.target.value)} />}</Field>
      </div>
    </Sheet>
  );
}

function SettleSheet({ employee: e, onClose }: { employee: Employee; onClose: () => void }) {
  const toast = useToast();
  const refresh = useRefresh(e.id);
  const accounts = useLookups().data?.accounts ?? [];
  const [key] = useIdempotencyKey();
  const suggested = nextPeriod(e);
  const [from, setFrom] = useState(suggested.from);
  const [to, setTo] = useState(suggested.to);
  const [units, setUnits] = useState(String(payUnits(e.payPeriod, suggested.from, suggested.to)));
  const [accountId, setAccountId] = useState<number | null>(null);
  const [notes, setNotes] = useState('');
  const [error, setError] = useState<string>();

  const u = parseNumber(units) ?? 0;
  const gross = round2((Number.isNaN(u) ? 0 : u) * e.rate);
  const due = round2(gross + e.openBonuses - e.openDeductions - e.openAdvances);
  const net = Math.max(0, due);

  const changeDates = (f: string, t: string) => {
    setFrom(f);
    setTo(t);
    setUnits(String(payUnits(e.payPeriod, f, t)));
    setError(undefined);
  };
  const most = maxUnits(e.payPeriod, from, to);
  const unitWord = e.payPeriod === 'Daily' ? 'الأيام' : e.payPeriod === 'Weekly' ? 'الأسابيع' : 'الأشهر';

  const save = useMutation({
    mutationFn: () => payrollApi.settle(e.id, { idempotencyKey: key, periodFrom: from, periodTo: to, units: u, financialAccountId: net > 0 ? accountId : null, notes: notes.trim() || null }),
    onSuccess: (data) => {
      refresh(data);
      toast.success(net > 0 ? `تم صرف راتب ${formatMoney(net)} لـ ${e.fullName}` : 'تم تسجيل المخالصة');
      onClose();
    },
    onError: (err) => setError(errorMessage(err)),
  });

  const submit = () => {
    if (!from || !to || to < from) return setError('حدد الفترة');
    if (!u || Number.isNaN(u) || u <= 0) return setError(`أدخل عدد ${unitWord}`);
    if (u > most + 0.001) return setError(`عدد ${unitWord} (${u}) أكبر مما في الفترة (${most})`);
    if (net > 0 && !accountId) return setError('اختر الحساب الذي يُصرف منه الراتب');
    setError(undefined);
    save.mutate();
  };

  return (
    <Sheet open onClose={onClose} size="lg" title={`مخالصة — ${e.fullName}`}
      footer={
        <div className="space-y-2">
          {error && <p role="alert" className="rounded-xl bg-red-50 p-3 text-sm text-danger">{error}</p>}
          <div className="flex items-center justify-between"><span className="text-sm">الصافي المستحق</span><b className="tabular text-2xl">{formatMoney(net)}</b></div>
          <Button block size="lg" variant="accent" loading={save.isPending} onClick={submit}>
            <Receipt className="size-5" /> {net > 0 ? 'اعتماد المخالصة وصرف الراتب' : 'اعتماد المخالصة'}
          </Button>
        </div>
      }>
      <div className="space-y-4">
        <div className="grid grid-cols-2 gap-3">
          <Field label="من">{(id) => <Input id={id} type="date" value={from} onChange={(ev) => changeDates(ev.target.value, to)} />}</Field>
          <Field label="إلى">{(id) => <Input id={id} type="date" value={to} onChange={(ev) => changeDates(from, ev.target.value)} />}</Field>
        </div>
        <Field label={`عدد ${e.payPeriod === 'Daily' ? 'أيام العمل' : e.payPeriod === 'Weekly' ? 'الأسابيع' : 'الأشهر'}`}
          hint={`${rateLabel(e.payPeriod, e.rate)} — عدّله إذا غاب العامل أو عمل جزءًا من الفترة`}>
          {(id, d) => <Input id={id} aria-describedby={d} inputMode="decimal" className="ltr text-lg" value={units} invalid={u > most + 0.001}
            onChange={(ev) => { setUnits(ev.target.value); setError(undefined); }} />}
        </Field>

        <Card className="p-4">
          <dl className="space-y-1 text-sm">
            <Line label="الراتب" value={gross} />
            {e.openBonuses > 0 && <Line label="+ مكافآت" value={e.openBonuses} />}
            {e.openDeductions > 0 && <Line label="− خصومات" value={e.openDeductions} />}
            {e.openAdvances > 0 && <Line label="− سلف" value={e.openAdvances} />}
            <div className="flex justify-between border-t border-line pt-1 font-bold"><dt>الصافي</dt><dd className="tabular">{formatMoney(net)}</dd></div>
          </dl>
          {due < 0 && <p className="mt-2 rounded-lg bg-amber-50 p-2.5 text-xs text-amber-800">السلف أكبر من الراتب: لا يُصرف شيء، ويبقى {formatMoney(-due)} سلفة تُخصم من المخالصة القادمة.</p>}
        </Card>

        {net > 0 && <AccountChips accounts={accounts} value={accountId} onChange={(id) => { setAccountId(id); setError(undefined); }} label="يُصرف من حساب" />}
        <Field label="ملاحظات">{(id) => <TextArea id={id} rows={2} value={notes} onChange={(ev) => setNotes(ev.target.value)} />}</Field>
      </div>
    </Sheet>
  );
}

function VoidSheet({ target, employeeId, onClose }: {
  target: { kind: 'entry'; item: PayrollEntry } | { kind: 'settlement'; item: PayrollSettlement };
  employeeId: number;
  onClose: () => void;
}) {
  const toast = useToast();
  const refresh = useRefresh(employeeId);
  const [reason, setReason] = useState('');
  const save = useMutation({
    mutationFn: () => target.kind === 'entry' ? payrollApi.voidEntry(target.item.id, reason.trim()) : payrollApi.voidSettlement(target.item.id, reason.trim()),
    onSuccess: (data) => { refresh(data); toast.success('تم الإلغاء'); onClose(); },
    onError: (e) => toast.error(errorMessage(e)),
  });
  const what = target.kind === 'settlement' ? `المخالصة ${target.item.number}` : ENTRY_TYPE[target.item.type].label;
  const money = target.kind === 'settlement' ? target.item.net : target.item.type === 'Advance' ? target.item.amount : 0;
  return (
    <Sheet open onClose={onClose} title={`إلغاء ${what}`}
      footer={<Button block size="lg" variant="danger" loading={save.isPending} disabled={!reason.trim()} onClick={() => save.mutate()}>تأكيد الإلغاء</Button>}>
      <div className="space-y-3">
        <p className="text-sm text-muted">
          {money > 0 ? `يعود ${formatMoney(money)} إلى الحساب الذي صُرف منه. ` : ''}
          {target.kind === 'settlement' && 'وترجع السلف والخصومات والمكافآت التي دخلت فيها لتنتظر المخالصة القادمة.'}
        </p>
        <Field label="سبب الإلغاء" required>{(id) => <TextArea id={id} rows={2} value={reason} onChange={(e) => setReason(e.target.value)} />}</Field>
      </div>
    </Sheet>
  );
}
