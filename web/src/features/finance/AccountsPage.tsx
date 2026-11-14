import { useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowDownLeft, ArrowLeftRight, ArrowRight, ArrowUpRight, Ban, Banknote, History, Landmark, Pencil, Plus, Scale, Smartphone } from 'lucide-react';
import { accountsApi, type Account, type AccountTransfer } from '@/api/finance';
import type { AccountType } from '@/api/endpoints';
import { useAuth } from '@/auth/AuthProvider';
import { P } from '@/auth/permissions';
import { Button } from '@/components/ui/Button';
import { Field, Input, Select, Switch } from '@/components/ui/Field';
import { Badge, EmptyState, ErrorState, PageHeader, Pagination, Spinner } from '@/components/ui/Feedback';
import { DateRangeFilter, type DateRangeValue } from '@/components/ui/DateRange';
import { Stat } from '@/components/ui/Layout';
import { Sheet } from '@/components/ui/Sheet';
import { useToast } from '@/components/ui/Toast';
import { cn } from '@/lib/cn';
import { errorMessage } from '@/lib/forms';
import { formatDateTime, formatMoney } from '@/lib/format';
import { useIdempotencyKey } from '@/lib/hooks';
import { parseNumber } from '@/lib/numbers';

const TYPE_LABEL: Record<AccountType, string> = { Cash: 'نقدي', Bank: 'بنك', Wallet: 'محفظة' };
const TYPE_ICON = { Cash: Banknote, Bank: Landmark, Wallet: Smartphone } as const;

function invalidateMoney(queryClient: ReturnType<typeof useQueryClient>) {
  for (const k of ['accounts', 'account', 'account-statement', 'lookups', 'account-transfers']) void queryClient.invalidateQueries({ queryKey: [k] });
}

export function AccountsPage() {
  const { can } = useAuth();
  const [editing, setEditing] = useState<Account | 'new' | null>(null);
  const [transferring, setTransferring] = useState(false);
  const [showTransfers, setShowTransfers] = useState(false);
  const query = useQuery({ queryKey: ['accounts'], queryFn: () => accountsApi.list(true) });

  if (query.isPending) return <Spinner />;
  if (query.isError) return <ErrorState message={errorMessage(query.error)} onRetry={() => void query.refetch()} />;
  const active = query.data.filter((a) => a.isActive);
  const total = active.reduce((s, a) => s + a.currentBalance, 0);
  const pending = active.reduce((s, a) => s + a.pendingTransfers, 0);

  return (
    <>
      <PageHeader title="الحسابات المالية" subtitle="الصندوق والبنوك والمحافظ" />
      <div className="mb-4 grid grid-cols-2 gap-2">
        <Stat label="مجموع الأرصدة" value={formatMoney(total)} tone={total < 0 ? 'danger' : 'default'} />
        <Stat label="حوالات بانتظار التحقق" value={formatMoney(pending)} tone={pending > 0 ? 'warning' : 'default'} hint="غير محسوبة في الأرصدة" />
      </div>
      {can(P.AccountsManage) && (
        <div className="mb-4 grid grid-cols-2 gap-2">
          <Button variant="secondary" onClick={() => setTransferring(true)}><ArrowLeftRight className="size-4" /> تحويل بين حسابات</Button>
          <Button variant="outline" onClick={() => setEditing('new')}><Plus className="size-4" /> حساب جديد</Button>
          <Button variant="outline" className="col-span-2" onClick={() => setShowTransfers(true)}><History className="size-4" /> سجل التحويلات بين الحسابات</Button>
        </div>
      )}
      <ul className="grid grid-cols-1 gap-2 md:grid-cols-2">
        {query.data.map((a) => {
          const Icon = TYPE_ICON[a.type];
          return (
            <li key={a.id}>
              <Link to={`/accounts/${a.id}`} className={cn('flex items-center gap-3 rounded-card border border-line bg-surface p-4 hover:border-brand-200', !a.isActive && 'opacity-60')}>
                <span className="flex size-11 shrink-0 items-center justify-center rounded-xl bg-brand-50 text-brand-700"><Icon className="size-5" /></span>
                <div className="min-w-0 flex-1">
                  <p className="flex items-center gap-2 font-semibold"><span className="truncate">{a.name}</span>{!a.isActive && <Badge tone="danger">معطّل</Badge>}</p>
                  <p className="text-xs text-muted">{TYPE_LABEL[a.type]}{a.accountNumber && <> · <span className="ltr inline-block">{a.accountNumber}</span></>}</p>
                  {a.pendingTransfers > 0 && <p className="text-xs text-warning">+{formatMoney(a.pendingTransfers)} بانتظار التحقق</p>}
                </div>
                <span className={cn('tabular shrink-0 text-lg font-bold', a.currentBalance < 0 && 'text-danger')}>{formatMoney(a.currentBalance)}</span>
              </Link>
            </li>
          );
        })}
      </ul>
      {editing && <AccountSheet account={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />}
      {transferring && <TransferSheet accounts={active} onClose={() => setTransferring(false)} />}
      {showTransfers && <TransfersLogSheet onClose={() => setShowTransfers(false)} />}
    </>
  );
}

function AccountSheet({ account, onClose }: { account: Account | null; onClose: () => void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const [name, setName] = useState(account?.name ?? '');
  const [type, setType] = useState<AccountType>(account?.type ?? 'Bank');
  const [provider, setProvider] = useState(account?.providerName ?? '');
  const [holder, setHolder] = useState(account?.holderName ?? '');
  const [number, setNumber] = useState(account?.accountNumber ?? '');
  const [active, setActive] = useState(account?.isActive ?? true);
  const [error, setError] = useState<string>();

  const save = useMutation({
    mutationFn: () => {
      const input = { name: name.trim(), type, providerName: provider.trim() || null, holderName: holder.trim() || null, accountNumber: number.trim() || null, isActive: active };
      return account ? accountsApi.update(account.id, input) : accountsApi.create(input);
    },
    onSuccess: () => { invalidateMoney(queryClient); toast.success('تم حفظ الحساب'); onClose(); },
    onError: (e) => setError(errorMessage(e)),
  });

  return (
    <Sheet open onClose={onClose} title={account ? 'تعديل الحساب' : 'حساب جديد'}
      footer={<Button block size="lg" loading={save.isPending} onClick={() => (name.trim() ? save.mutate() : setError('اسم الحساب مطلوب'))}>حفظ</Button>}>
      <div className="space-y-4">
        {error && <p role="alert" className="rounded-xl bg-red-50 p-3 text-sm text-danger">{error}</p>}
        <Field label="اسم الحساب" required>{(id) => <Input id={id} value={name} onChange={(e) => setName(e.target.value)} placeholder="مثال: بنك فلسطين — حساب 2" />}</Field>
        <Field label="النوع">
          {(id) => (
            <Select id={id} value={type} onChange={(e) => setType(e.target.value as AccountType)}>
              <option value="Cash">نقدي (صندوق)</option>
              <option value="Bank">بنك</option>
              <option value="Wallet">محفظة إلكترونية</option>
            </Select>
          )}
        </Field>
        <Field label="البنك أو مزود الخدمة">{(id) => <Input id={id} value={provider} onChange={(e) => setProvider(e.target.value)} />}</Field>
        <Field label="اسم صاحب الحساب">{(id) => <Input id={id} value={holder} onChange={(e) => setHolder(e.target.value)} />}</Field>
        <Field label="رقم الحساب / المحفظة">{(id) => <Input id={id} className="ltr" value={number} onChange={(e) => setNumber(e.target.value)} />}</Field>
        {account && <Switch label="الحساب فعّال" description="الحساب المعطّل لا يظهر عند البيع والسندات" checked={active} onChange={setActive} />}
        {!account && <p className="text-sm text-muted">بعد الإنشاء يمكنك ضبط الرصيد الافتتاحي من صفحة الحساب.</p>}
      </div>
    </Sheet>
  );
}

function TransferSheet({ accounts, onClose }: { accounts: Account[]; onClose: () => void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const [key] = useIdempotencyKey();
  const [from, setFrom] = useState(String(accounts.find((a) => a.type === 'Cash')?.id ?? accounts[0]?.id ?? ''));
  const [to, setTo] = useState(String(accounts.find((a) => a.type === 'Bank')?.id ?? accounts[1]?.id ?? ''));
  const [amount, setAmount] = useState('');
  const [notes, setNotes] = useState('');
  const [error, setError] = useState<string>();
  const fromAccount = accounts.find((a) => String(a.id) === from);

  const save = useMutation({
    mutationFn: () => accountsApi.transfer({ idempotencyKey: key, fromAccountId: Number(from), toAccountId: Number(to), amount: parseNumber(amount)!, notes: notes.trim() || null }),
    onSuccess: (t) => { invalidateMoney(queryClient); toast.success(`تم التحويل ${t.number}`); onClose(); },
    onError: (e) => setError(errorMessage(e)),
  });

  const submit = () => {
    const n = parseNumber(amount);
    if (!n || Number.isNaN(n) || n <= 0) return setError('أدخل مبلغًا صحيحًا');
    if (from === to) return setError('اختر حسابين مختلفين');
    setError(undefined);
    save.mutate();
  };

  return (
    <Sheet open onClose={onClose} title="تحويل بين حسابات المعرض"
      footer={<Button block size="lg" loading={save.isPending} onClick={submit}>تحويل</Button>}>
      <div className="space-y-4">
        {error && <p role="alert" className="rounded-xl bg-red-50 p-3 text-sm text-danger">{error}</p>}
        <p className="text-sm text-muted">مثال: إيداع نقدية الصندوق في البنك. لا يُحسب إيرادًا ولا مصروفًا.</p>
        <Field label="من حساب" hint={fromAccount ? `الرصيد ${formatMoney(fromAccount.currentBalance)}` : undefined}>
          {(id, h) => <Select id={id} aria-describedby={h} value={from} onChange={(e) => setFrom(e.target.value)}>{accounts.map((a) => <option key={a.id} value={a.id}>{a.name}</option>)}</Select>}
        </Field>
        <Field label="إلى حساب">
          {(id) => <Select id={id} value={to} onChange={(e) => setTo(e.target.value)}>{accounts.map((a) => <option key={a.id} value={a.id}>{a.name}</option>)}</Select>}
        </Field>
        <Field label="المبلغ" required>{(id) => <Input id={id} inputMode="decimal" className="ltr text-lg" value={amount} onChange={(e) => setAmount(e.target.value)} />}</Field>
        <Field label="ملاحظات">{(id) => <Input id={id} value={notes} onChange={(e) => setNotes(e.target.value)} />}</Field>
      </div>
    </Sheet>
  );
}

/** Transfers between the store's own accounts, newest first; a wrong one is cancelled here (the money goes back). */
function TransfersLogSheet({ onClose }: { onClose: () => void }) {
  const [page, setPage] = useState(1);
  const [cancelling, setCancelling] = useState<AccountTransfer | null>(null);
  const query = useQuery({ queryKey: ['account-transfers', page], queryFn: () => accountsApi.transfers(page), placeholderData: keepPreviousData });
  return (
    <Sheet open onClose={onClose} title="سجل التحويلات بين الحسابات" size="lg">
      {query.isPending ? <Spinner /> : query.isError ? <ErrorState message={errorMessage(query.error)} onRetry={() => void query.refetch()} /> : (
        query.data.items.length === 0 ? <EmptyState title="لا توجد تحويلات" /> : (
          <>
            <ul className="divide-y divide-line">
              {query.data.items.map((t) => (
                <li key={t.id} className={cn('flex flex-wrap items-center justify-between gap-2 py-3', t.voidedAt && 'opacity-70')}>
                  <div className="min-w-0">
                    <p className="font-semibold">{t.fromAccountName} ← {t.toAccountName}{t.voidedAt && <span className="ms-2"><Badge tone="danger">ملغى</Badge></span>}</p>
                    <p className="text-xs text-muted"><span className="ltr inline-block">{t.number}</span> · {formatDateTime(t.date)}{t.createdByName && ` · ${t.createdByName}`}{t.notes && ` · ${t.notes}`}</p>
                    {t.voidedAt && <p className="text-xs text-danger">أُلغي في {formatDateTime(t.voidedAt)} — {t.voidReason}</p>}
                  </div>
                  <div className="flex items-center gap-2">
                    <span className={cn('tabular font-bold', t.voidedAt && 'line-through')}>{formatMoney(t.amount)}</span>
                    {!t.voidedAt && <Button size="sm" variant="outline" onClick={() => setCancelling(t)}><Ban className="size-4 text-danger" /> إلغاء</Button>}
                  </div>
                </li>
              ))}
            </ul>
            <Pagination page={query.data.page} totalPages={query.data.totalPages} onChange={setPage} />
          </>
        )
      )}
      {cancelling && <CancelTransferSheet transfer={cancelling} onClose={() => setCancelling(null)} />}
    </Sheet>
  );
}

function CancelTransferSheet({ transfer, onClose }: { transfer: AccountTransfer; onClose: () => void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const [reason, setReason] = useState('');
  const [error, setError] = useState<string>();
  const save = useMutation({
    mutationFn: () => accountsApi.voidTransfer(transfer.id, reason.trim()),
    onSuccess: () => { invalidateMoney(queryClient); toast.success(`تم إلغاء التحويل ${transfer.number}`); onClose(); },
    onError: (e) => setError(errorMessage(e)),
  });
  return (
    <Sheet open onClose={onClose} title={`إلغاء التحويل ${transfer.number}`}
      footer={<Button block size="lg" variant="danger" loading={save.isPending} onClick={() => (reason.trim() ? save.mutate() : setError('سبب الإلغاء مطلوب'))}>تأكيد الإلغاء</Button>}>
      <div className="space-y-3">
        <p className="text-sm leading-6 text-muted">
          يرجع المبلغ <b className="tabular">{formatMoney(transfer.amount)}</b> من «{transfer.toAccountName}» إلى «{transfer.fromAccountName}».
          يجب أن يكون المبلغ ما زال موجودًا في «{transfer.toAccountName}». يبقى التحويل ظاهرًا بحالة «ملغى».
        </p>
        <Field label="سبب الإلغاء" required error={error}>
          {(id, d) => <Input id={id} aria-describedby={d} value={reason} onChange={(e) => { setReason(e.target.value); setError(undefined); }} />}
        </Field>
      </div>
    </Sheet>
  );
}

export function AccountDetailPage() {
  const id = Number(useParams().id);
  const { can } = useAuth();
  const navigate = useNavigate();
  const [range, setRange] = useState<DateRangeValue>({ preset: 'thisMonth' });
  const [page, setPage] = useState(1);
  const [editing, setEditing] = useState(false);
  const [opening, setOpening] = useState(false);
  const account = useQuery({ queryKey: ['account', id], queryFn: () => accountsApi.get(id) });
  const statement = useQuery({
    queryKey: ['account-statement', id, range.from, range.to, page],
    queryFn: () => accountsApi.statement(id, { from: range.from, to: range.to, page, pageSize: 30 }),
    placeholderData: keepPreviousData,
  });

  if (account.isPending) return <Spinner />;
  if (account.isError) return <ErrorState message={errorMessage(account.error)} onRetry={() => void account.refetch()} />;
  const a = account.data;

  return (
    <div className="space-y-4">
      <button type="button" onClick={() => navigate('/accounts')} className="flex h-10 items-center gap-1 text-sm text-muted"><ArrowRight className="size-4" /> الحسابات</button>
      <div className="flex items-start justify-between gap-2">
        <div>
          <h1 className="text-xl font-bold">{a.name}</h1>
          <p className="text-sm text-muted">{TYPE_LABEL[a.type]}{a.providerName && ` · ${a.providerName}`}{a.holderName && ` · ${a.holderName}`}</p>
          {a.accountNumber && <p className="ltr text-sm text-muted">{a.accountNumber}</p>}
        </div>
        {can(P.AccountsManage) && <Button variant="outline" onClick={() => setEditing(true)}><Pencil className="size-4" /> تعديل</Button>}
      </div>
      <div className="grid grid-cols-2 gap-2">
        <Stat label="الرصيد الحالي" value={formatMoney(a.currentBalance)} tone={a.currentBalance < 0 ? 'danger' : 'default'} />
        <Stat label="الرصيد الافتتاحي" value={formatMoney(a.openingBalance)} onClick={can(P.AccountsManage) ? () => setOpening(true) : undefined}
          hint={can(P.AccountsManage) ? 'اضغط للتعديل' : undefined} />
      </div>
      {a.pendingTransfers > 0 && (
        <Link to="/transfers" className="block rounded-xl bg-amber-50 p-3 text-sm font-medium text-amber-800">
          حوالات بانتظار التحقق على هذا الحساب: {formatMoney(a.pendingTransfers)} — غير محسوبة في الرصيد
        </Link>
      )}

      <h2 className="font-bold">كشف الحركات</h2>
      <DateRangeFilter value={range} onChange={(r) => { setRange(r); setPage(1); }} />
      {statement.isPending ? <Spinner /> : statement.isError ? <ErrorState message={errorMessage(statement.error)} onRetry={() => void statement.refetch()} /> : (
        <>
          <div className="grid grid-cols-2 gap-2 sm:grid-cols-4">
            <Stat label="رصيد بداية الفترة" value={formatMoney(statement.data.openingBalance)} />
            <Stat label="الداخل" value={formatMoney(statement.data.totalIn)} tone="success" />
            <Stat label="الخارج" value={formatMoney(statement.data.totalOut)} tone="danger" />
            <Stat label="رصيد نهاية الفترة" value={formatMoney(statement.data.closingBalance)} />
          </div>
          {statement.data.movements.items.length === 0 ? <EmptyState title="لا توجد حركات في هذه الفترة" /> : (
            <ul className="divide-y divide-line rounded-card border border-line bg-surface">
              {statement.data.movements.items.map((m) => (
                <li key={m.id} className="flex items-start gap-3 p-3">
                  <span className={cn('mt-0.5 flex size-9 shrink-0 items-center justify-center rounded-full', m.direction === 'In' ? 'bg-green-50 text-success' : 'bg-red-50 text-danger')}>
                    {m.direction === 'In' ? <ArrowDownLeft className="size-5" /> : <ArrowUpRight className="size-5" />}
                  </span>
                  <div className="min-w-0 flex-1">
                    <div className="flex items-start justify-between gap-2">
                      <p className="font-semibold">{m.categoryName}</p>
                      <p className={cn('tabular shrink-0 font-bold', m.direction === 'In' ? 'text-success' : 'text-danger')}>
                        {m.direction === 'In' ? '+' : '−'}{formatMoney(m.amount)}
                      </p>
                    </div>
                    {m.description && <p className="text-sm">{m.description}</p>}
                    <p className="text-xs text-muted">
                      {formatDateTime(m.date)}{m.sourceNumber && <> · <span className="ltr inline-block">{m.sourceNumber}</span></>}{m.userName && <> · {m.userName}</>}
                    </p>
                    <p className="tabular text-xs text-muted">الرصيد بعدها: {formatMoney(m.balanceAfter)}</p>
                  </div>
                </li>
              ))}
            </ul>
          )}
          <Pagination page={statement.data.movements.page} totalPages={statement.data.movements.totalPages} onChange={setPage} />
        </>
      )}
      {editing && <AccountSheet account={a} onClose={() => setEditing(false)} />}
      {opening && <OpeningSheet account={a} onClose={() => setOpening(false)} />}
    </div>
  );
}

function OpeningSheet({ account, onClose }: { account: Account; onClose: () => void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const [value, setValue] = useState(String(account.openingBalance));
  const [notes, setNotes] = useState('');
  const [error, setError] = useState<string>();
  const n = parseNumber(value);
  const save = useMutation({
    mutationFn: () => accountsApi.setOpening(account.id, n!, notes.trim() || null),
    onSuccess: () => { invalidateMoney(queryClient); toast.success('تم ضبط الرصيد الافتتاحي'); onClose(); },
    onError: (e) => setError(errorMessage(e)),
  });
  return (
    <Sheet open onClose={onClose} title="الرصيد الافتتاحي"
      footer={<Button block size="lg" loading={save.isPending} onClick={() => (n === undefined || Number.isNaN(n) || n < 0 ? setError('أدخل مبلغًا صحيحًا') : save.mutate())}>حفظ</Button>}>
      <div className="space-y-4">
        {error && <p role="alert" className="rounded-xl bg-red-50 p-3 text-sm text-danger">{error}</p>}
        <p className="flex items-start gap-2 rounded-xl bg-brand-50 p-3 text-sm leading-6">
          <Scale className="mt-1 size-4 shrink-0" />
          المبلغ الموجود في هذا الحساب قبل بدء استخدام النظام. أي تغيير يُسجَّل كحركة «رصيد افتتاحي» موثقة في الكشف، ولا يُحسب إيرادًا.
        </p>
        <Field label="الرصيد الافتتاحي (₪)" required>{(id) => <Input id={id} inputMode="decimal" className="ltr text-lg" value={value} onChange={(e) => setValue(e.target.value)} />}</Field>
        <Field label="ملاحظة">{(id) => <Input id={id} value={notes} onChange={(e) => setNotes(e.target.value)} placeholder="مثال: جرد الصندوق صباح أول يوم" />}</Field>
      </div>
    </Sheet>
  );
}
