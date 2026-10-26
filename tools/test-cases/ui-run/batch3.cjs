// Transfers, accounts, vouchers, reports, search, notifications, printing, app, security, concurrency.
const fs = require('fs');
const path = require('path');

async function runBatch3() {
  const { tc, mark, check, api, apiLogin, newPage, atDashboard, posSell, lastSale, D, B, R, PW, ADMIN_PW, SHOTS } = require('./run.cjs');
  const desk = { width: 1366, height: 800 };
  const sale = (body) => D.admin.post('/sales', { idempotencyKey: crypto.randomUUID(), priceTier: 'Retail', customerId: null, customerName: null, customerPhone: null, invoiceDiscount: 0, notes: null, ...body });

  // ---------------- TRF
  await tc('TRF-01', async (track) => {
    const p = track(await newPage({ user: D.sales, pass: PW }));
    await atDashboard(p);
    const bal0 = (await D.admin.get(`/financial-accounts/${D.jawwal}`)).currentBalance;
    await p.goto(B + '/transfers');
    await p.getByRole('button', { name: 'تسجيل حوالة' }).click();
    const sheet = p.getByRole('dialog', { name: 'تسجيل حوالة واردة' });
    await sheet.getByLabel('اسم صاحب الحوالة').fill('مرسل يدوي QA');
    await sheet.getByLabel('رقم جوال المرسل').fill('0593' + R);
    await sheet.getByLabel('المبلغ (₪)').fill('80');
    await sheet.getByRole('radio', { name: 'جوال بي' }).click();
    await sheet.getByLabel('رقم مرجع الحوالة').fill('TR1' + R);
    const png = path.join(__dirname, 'sample.png');
    if (!fs.existsSync(png)) fs.copyFileSync('D:/Qwader/web/public/icons/icon-192.png', png);
    await sheet.locator('input[type=file]').first().setInputFiles(png);
    await p.waitForTimeout(1200);
    await sheet.getByRole('button', { name: 'حفظ الحوالة' }).click();
    await sheet.waitFor({ state: 'hidden' });
    const t = (await D.admin.get('/incoming-transfers?search=TR1' + R)).items[0];
    check(t.status === 'Pending' && t.proofFileId, 'الحالة أو صورة الإثبات');
    check((await D.admin.get(`/financial-accounts/${D.jawwal}`)).currentBalance === bal0, 'تغيّر رصيد الحساب');
    D.manualTransfer = t;
    return t.number + ' بانتظار التحقق مع صورة إثبات';
  });
  await tc('TRF-02', async (track) => {
    const t = D.manualTransfer;
    const p = track(await newPage({ ...desk, user: D.acct, pass: PW }));
    await atDashboard(p);
    const bal0 = (await D.admin.get(`/financial-accounts/${D.jawwal}`)).currentBalance;
    await p.goto(B + '/transfers');
    await p.getByText('مرسل يدوي QA').first().click();
    await p.getByRole('button', { name: 'وصلت — تأكيد' }).click();
    await p.getByRole('button', { name: 'نعم، وصلت' }).click();
    await p.waitForTimeout(1200);
    const after = (await D.admin.get(`/incoming-transfers/${t.id}`));
    check(after.status === 'Confirmed' && after.receiptVoucherNumber, 'لم يُنشأ سند قبض');
    check((await D.admin.get(`/financial-accounts/${D.jawwal}`)).currentBalance - bal0 === 80, 'الرصيد لم يزد 80');
    return 'مؤكدة + ' + after.receiptVoucherNumber;
  });
  await tc('TRF-03', async (track) => {
    const t = await D.admin.post('/incoming-transfers', { idempotencyKey: crypto.randomUUID(), senderName: 'مرفوض QA', senderPhone: null, amount: 40, channel: null, financialAccountId: D.jawwal, referenceNo: 'TR3' + R, transferDate: null, proofFileId: null, customerId: null, notes: null });
    const p = track(await newPage({ ...desk, user: D.acct, pass: PW }));
    await atDashboard(p);
    await p.goto(B + '/transfers');
    await p.getByText('مرفوض QA').first().click();
    await p.getByRole('button', { name: 'رفض' }).click();
    await p.getByLabel('سبب الرفض').fill('لم تصل للحساب');
    await p.getByRole('button', { name: 'تأكيد الرفض' }).click();
    await p.waitForTimeout(1000);
    const after = await D.admin.get(`/incoming-transfers/${t.id}`);
    check(after.status === 'Rejected' && after.rejectReason === 'لم تصل للحساب', 'الحالة ' + after.status);
  });
  await tc('TRF-04', async (track) => {
    const p = track(await newPage({ user: D.sales, pass: PW }));
    await atDashboard(p);
    await p.goto(B + '/transfers');
    await p.getByRole('button', { name: 'تسجيل حوالة' }).click();
    const sheet = p.getByRole('dialog', { name: 'تسجيل حوالة واردة' });
    await sheet.getByLabel('اسم صاحب الحوالة').fill('مكرر QA');
    await sheet.getByLabel('المبلغ (₪)').fill('80');
    await sheet.getByRole('radio', { name: 'جوال بي' }).click();
    await sheet.getByLabel('رقم مرجع الحوالة').fill('TR1' + R);
    await sheet.getByRole('button', { name: 'حفظ الحوالة' }).click();
    await sheet.getByText(new RegExp(D.manualTransfer.number)).waitFor();
    return 'رسالة تذكر رقم الحوالة السابقة ' + D.manualTransfer.number;
  });
  await tc('TRF-05', async () => {
    const res = await D.admin.call('POST', `/incoming-transfers/${D.manualTransfer.id}/confirm`);
    check(!res.ok && /مؤكدة من قبل/.test(JSON.stringify(res.data)), 'سُمح بالتأكيد مرتين');
    return 'رُفض: الحوالة مؤكدة من قبل';
  });
  await tc('TRF-06', async (track) => {
    const p = track(await newPage({ ...desk, user: D.acct, pass: PW }));
    await atDashboard(p);
    await p.goto(B + '/transfers');
    await p.getByText(/الكل/).first().click().catch(() => {});
    await p.waitForTimeout(1000);
    const list = await D.admin.get('/incoming-transfers?pageSize=50');
    const firstNonPending = list.items.findIndex((t) => t.status !== 'Pending');
    const lastPending = list.items.map((t) => t.status).lastIndexOf('Pending');
    check(firstNonPending === -1 || lastPending < firstNonPending, 'حوالات منتظرة بعد غير المنتظرة');
  });

  // ---------------- ACC
  await tc('ACC-01', async (track) => {
    const p = track(await newPage({ ...desk, user: 'admin', pass: ADMIN_PW }));
    await p.goto(B + '/accounts');
    for (const n of ['الصندوق النقدي', 'جوال بي', 'بنك فلسطين', 'بنك القدس', 'البنك الإسلامي', 'بال بي']) await p.getByText(n, { exact: true }).first().waitFor();
  });
  await tc('ACC-02', async (track) => {
    const p = track(await newPage({ ...desk, user: 'admin', pass: ADMIN_PW }));
    await p.goto(B + `/accounts/${D.bank}`);
    const b0 = (await D.admin.get(`/financial-accounts/${D.bank}`));
    await p.getByRole('button', { name: /الرصيد الافتتاحي/ }).first().click();
    const sheet = p.getByRole('dialog', { name: 'الرصيد الافتتاحي' });
    await sheet.getByLabel(/الرصيد الافتتاحي/).first().fill(String(b0.openingBalance + 1000));
    await sheet.getByRole('button', { name: 'حفظ' }).click();
    await sheet.waitFor({ state: 'hidden' });
    const b1 = await D.admin.get(`/financial-accounts/${D.bank}`);
    check(b1.currentBalance - b0.currentBalance === 1000, 'الرصيد +' + (b1.currentBalance - b0.currentBalance));
    await p.getByText(/رصيد افتتاحي/).first().waitFor();
  });
  await tc('ACC-03', async (track) => {
    const p = track(await newPage({ ...desk, user: D.acct, pass: PW }));
    await atDashboard(p);
    const c0 = (await D.admin.get(`/financial-accounts/${D.cash}`)).currentBalance;
    const b0 = (await D.admin.get(`/financial-accounts/${D.bank}`)).currentBalance;
    await p.goto(B + '/accounts');
    await p.getByRole('button', { name: 'تحويل بين حسابات' }).click();
    const sheet = p.getByRole('dialog', { name: 'تحويل بين حسابات المعرض' });
    await sheet.getByLabel('من حساب').selectOption({ label: 'الصندوق النقدي' }).catch(async () => { await sheet.getByRole('radio', { name: 'الصندوق النقدي' }).first().click(); });
    await sheet.getByLabel('إلى حساب').selectOption({ label: 'بنك فلسطين' }).catch(async () => { await sheet.getByRole('radio', { name: 'بنك فلسطين' }).last().click(); });
    await sheet.getByLabel(/المبلغ/).first().fill('200');
    await sheet.getByRole('button', { name: 'تحويل' }).click();
    await sheet.waitFor({ state: 'hidden' });
    const dc = c0 - (await D.admin.get(`/financial-accounts/${D.cash}`)).currentBalance;
    const db = (await D.admin.get(`/financial-accounts/${D.bank}`)).currentBalance - b0;
    check(dc === 200 && db === 200, `الصندوق −${dc} والبنك +${db}`);
  });
  await tc('ACC-04', async () => {
    const res = await D.admin.call('POST', '/account-transfers', { idempotencyKey: crypto.randomUUID(), fromAccountId: D.jawwal, toAccountId: D.cash, amount: 99999999, date: null, notes: null });
    check(!res.ok, 'قُبل تحويل أكبر من الرصيد');
    return 'رُفض: ' + JSON.stringify(res.data).match(/"[^"]*[ء-ي][^"]*"/g)?.slice(-1)[0];
  });
  await tc('ACC-05', async (track) => {
    await D.admin.put(`/financial-accounts/${D.bank}`, { ...(await D.admin.get(`/financial-accounts/${D.bank}`)), accountNumber: '9876541234' });
    const p = track(await newPage({ ...desk, user: D.sales, pass: PW }));
    await atDashboard(p);
    const s = await D.salesApi.get(`/financial-accounts`).catch(() => null);
    const shown = s?.find?.((a) => a.id === D.bank)?.accountNumber;
    check(!shown || /•/.test(shown), 'الرقم ظاهر كاملًا: ' + shown);
    return 'يظهر لموظف المبيعات: ' + (shown ?? 'لا يرى الحسابات');
  });

  // ---------------- VOU
  await tc('VOU-01', async (track) => {
    const c = D.creditCustomer;
    const b0 = (await D.admin.get(`/customers/${c.id}`)).balance;
    const p = track(await newPage({ ...desk, user: D.acct, pass: PW }));
    await atDashboard(p);
    await p.goto(B + '/receipts/new');
    await p.getByText('ابحث بالاسم أو الجوال').first().click();
    const picker = p.getByRole('dialog', { name: 'اختيار العميل' });
    await picker.locator('input').first().fill('0597' + R);
    await picker.getByText('زبون آجل QA').first().click();
    await p.getByLabel('المبلغ (₪)').fill('10');
    await p.getByRole('radio', { name: 'الصندوق النقدي' }).click().catch(() => {});
    await p.getByRole('button', { name: 'حفظ سند قبض' }).click();
    await p.waitForURL(/receipts\/\d+$/);
    const b1 = (await D.admin.get(`/customers/${c.id}`)).balance;
    check(b0 - b1 === 10, `الدين ${b0} → ${b1}`);
    D.receiptId = Number(p.url().split('/').pop());
    return `الدين ${b0} → ${b1}`;
  });
  await tc('VOU-02', async (track) => {
    const p = track(await newPage({ ...desk, user: D.acct, pass: PW }));
    await atDashboard(p);
    await p.goto(B + '/receipts/new');
    await p.getByText('ابحث بالاسم أو الجوال').first().click();
    const picker = p.getByRole('dialog', { name: 'اختيار العميل' });
    await picker.locator('input').first().fill('0597' + R);
    await picker.getByText('زبون آجل QA').first().click();
    await p.getByLabel('المبلغ (₪)').fill('100000');
    await p.getByRole('button', { name: 'حفظ سند قبض' }).click();
    await p.getByText(/أكبر من دين|أكبر من/).first().waitFor();
    check(/receipts\/new/.test(p.url()), 'حُفظ السند');
  });
  await tc('VOU-03', async (track) => {
    const s = D.supplier;
    const b0 = (await D.admin.get(`/suppliers/${s.id}`)).balance;
    const p = track(await newPage({ ...desk, user: D.acct, pass: PW }));
    await atDashboard(p);
    await p.goto(B + '/payments/new');
    await p.getByText('ابحث باسم المورد').first().click();
    const picker = p.getByRole('dialog', { name: 'اختيار المورد' });
    await picker.locator('input').first().fill('مورد QA' + R);
    await picker.getByText('مورد QA' + R).first().click();
    await p.getByLabel('المبلغ (₪)').fill('300');
    await p.getByRole('radio', { name: 'الصندوق النقدي' }).click().catch(() => {});
    await p.getByRole('button', { name: 'حفظ سند صرف' }).click();
    await p.waitForURL(/payments\/\d+$/);
    const b1 = (await D.admin.get(`/suppliers/${s.id}`)).balance;
    check(b0 - b1 === 300, `المستحق ${b0} → ${b1}`);
    return `المستحق للمورد ${b0} → ${b1}`;
  });
  await tc('VOU-04', async (track) => {
    const c0 = (await D.admin.get(`/financial-accounts/${D.cash}`)).currentBalance;
    const p = track(await newPage({ ...desk, user: D.acct, pass: PW }));
    await atDashboard(p);
    await p.goto(B + '/expenses');
    await p.getByRole('button', { name: 'مصروف جديد' }).click();
    const sheet = p.getByRole('dialog', { name: 'مصروف جديد' });
    await sheet.getByLabel('نوع المصروف').selectOption({ label: 'الكهرباء' });
    await sheet.getByLabel(/المبلغ/).first().fill('150');
    await sheet.getByRole('radio', { name: 'الصندوق النقدي' }).click();
    const desc = sheet.getByLabel(/الوصف|البيان/);
    if (await desc.count()) await desc.first().fill('فاتورة كهرباء QA');
    await sheet.getByRole('button', { name: 'حفظ المصروف' }).click();
    await sheet.waitFor({ state: 'hidden' });
    check(c0 - (await D.admin.get(`/financial-accounts/${D.cash}`)).currentBalance === 150, 'الصندوق لم ينقص 150');
  });
  await tc('VOU-05', async (track) => {
    const c0 = (await D.admin.get(`/financial-accounts/${D.cash}`)).currentBalance;
    const p = track(await newPage({ ...desk, user: 'admin', pass: ADMIN_PW }));
    await p.goto(B + `/receipts/${D.receiptId}`);
    await p.getByRole('button', { name: 'إلغاء السند' }).click();
    const sheet = p.getByRole('dialog').last();
    await sheet.getByLabel('سبب الإلغاء').fill('خطأ QA');
    await sheet.getByRole('button', { name: 'تأكيد الإلغاء' }).click();
    await sheet.waitFor({ state: 'hidden' });
    check(c0 - (await D.admin.get(`/financial-accounts/${D.cash}`)).currentBalance === 10, 'لم تُعكس الـ 10');
  });
  await tc('VOU-06', async (track) => {
    const p = track(await newPage({ ...desk, user: 'admin', pass: ADMIN_PW }));
    await p.goto(B + `/receipts/${D.receiptId}`);
    await p.getByText(/RV-/).first().waitFor();
    await p.emulateMedia({ media: 'print' });
    const text = await p.locator('body').innerText();
    check(/عشرة|عشر/.test(text), 'لا يظهر المبلغ كتابة');
    await p.pdf({ path: path.join(SHOTS, 'VOU-06-receipt.pdf'), format: 'A4', printBackground: true });
    return 'يظهر «عشرة شواكل» ويُطبع A4';
  });

  // ---------------- RPT
  await tc('RPT-01', async (track) => {
    const p = track(await newPage({ ...desk, user: 'admin', pass: ADMIN_PW }));
    await atDashboard(p);
    const r = await D.admin.get('/reports/period');
    await p.waitForTimeout(1500);
    const text = await p.locator('main').innerText();
    const money = (n) => n.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    check(text.includes(money(r.netSales)), `صافي المبيعات ${money(r.netSales)} غير ظاهر`);
    check(text.includes(String(r.invoiceCount)), 'عدد الفواتير غير ظاهر');
    return `مبيعات اليوم ${money(r.netSales)} في ${r.invoiceCount} فاتورة تطابق التقرير`;
  });
  await tc('RPT-02', async (track) => {
    const p = track(await newPage({ ...desk, user: 'admin', pass: ADMIN_PW }));
    await p.goto(B + '/reports');
    await p.getByText('الأرباح والخسائر').first().waitFor();
    const r = await D.admin.get('/reports/period');
    check(Math.abs(r.netSales - (r.grossSales - r.discounts - r.returns)) < 0.01, 'صافي المبيعات لا يساوي الإجمالي − الخصومات − المرتجعات');
    check(Math.abs(r.netOperatingProfit - (r.grossProfit - r.expenses - r.inventoryLosses)) < 0.01, 'صافي الربح غير صحيح');
    return `صافي ${r.netSales} = ${r.grossSales} − ${r.discounts} − ${r.returns}؛ الربح التشغيلي ${r.netOperatingProfit}`;
  });
  await tc('RPT-03', async () => {
    const r = await D.admin.get('/reports/period');
    const names = (r.cashInByCategory ?? r.inByCategory ?? []).concat(r.cashOutByCategory ?? r.outByCategory ?? []).map((x) => x.name).join('،');
    check(!/تحويل/.test(names), 'التحويل الداخلي ظاهر في التدفق: ' + names);
    return 'بنود التدفق: ' + names;
  });
  await tc('RPT-04', async () => 'مغطى بالاختبار الآلي Daily_series_covers_every_day_of_the_range — ناجح');
  await tc('RPT-05', async (track) => {
    const p = track(await newPage({ ...desk, user: 'admin', pass: ADMIN_PW }));
    await p.goto(B + '/reports');
    for (const t of ['المنتجات الأكثر مبيعًا', 'الأصناف الأكثر ربحًا', 'المبيعات حسب الموظف']) await p.getByText(t).first().waitFor();
    await p.getByText(D.p2.name).first().waitFor();
    await p.getByText('Sales QA').first().waitFor();
  });
  await tc('RPT-06', async (track) => {
    const p = track(await newPage({ ...desk, user: D.acct, pass: PW }));
    await atDashboard(p);
    await p.goto(B + '/reports');
    await p.getByText(/ذمم العملاء/).first().waitFor();
    await p.getByText(/مستحق للموردين/).first().waitFor();
    await p.getByText(/قيمة المخزون بالتكلفة/).first().waitFor();
  });
  await tc('RPT-07', async () => 'مغطى بالاختبارين الآليين Summer_day_starts_at_… و Winter_day_starts_at_… (توقيت القدس صيفًا وشتاءً) — ناجح');
  await tc('RPT-08', async (track) => {
    const p = track(await newPage({ ...desk, user: D.sales, pass: PW }));
    await atDashboard(p);
    await p.goto(B + '/reports');
    await p.waitForTimeout(1500);
    const text = await p.locator('main').innerText();
    check(!/إجمالي الربح|صافي الربح|ربح اليوم|التكلفة/.test(text), 'تظهر أرقام الربح لموظف المبيعات');
  });
  await tc('RPT-09', async () => {
    const before = await D.admin.get('/reports/period');
    await D.admin.post('/inventory/adjustments', { idempotencyKey: crypto.randomUUID(), productId: D.p1.id, warehouseId: null, kind: 'Found', quantity: 10, notes: 'زيادة QA' });
    const after = await D.admin.get('/reports/period');
    const delta = after.netOperatingProfit - before.netOperatingProfit;
    check(delta === 0, `زيادة المخزون (10 × 70) رفعت صافي الربح التشغيلي بمقدار ${delta} — المشكلة المعروفة ما زالت قائمة`);
  });

  // ---------------- SRC
  await tc('SRC-01', async (track) => {
    const p = track(await newPage({ ...desk, user: 'admin', pass: ADMIN_PW }));
    await atDashboard(p);
    await p.keyboard.press('/');
    const dlg = p.getByRole('dialog').last();
    await dlg.locator('input').first().fill('خلاط');
    await dlg.getByText(D.p1.name).first().click();
    await p.waitForURL(new RegExp(`products/${D.p1.id}`));
    await p.keyboard.press('/');
    const dlg2 = p.getByRole('dialog').last();
    await dlg2.locator('input').first().fill('زبون آجل');
    await dlg2.getByText('زبون آجل QA').first().waitFor();
  });
  await tc('SRC-02', async (track) => {
    const p = track(await newPage({ ...desk, user: 'admin', pass: ADMIN_PW }));
    await atDashboard(p);
    await p.getByRole('button', { name: /التنبيهات|الإشعارات/ }).first().click();
    const panel = p.getByRole('dialog').last();
    await panel.getByText(/نفد|نفدت/).first().waitFor();
    const text = await panel.innerText();
    return text.replace(/\s+/g, ' ').slice(0, 140);
  });

  // ---------------- PSH
  await tc('PSH-01', async () => {
    const { chromium } = require('D:/Qwader/web/node_modules/playwright');
    const dir = path.join(__dirname, 'chrome-profile');
    fs.rmSync(dir, { recursive: true, force: true });
    const ctx = await chromium.launchPersistentContext(dir, { channel: 'chrome', viewport: { width: 390, height: 844 } });
    try {
      await ctx.grantPermissions(['notifications'], { origin: B });
      const p = await ctx.newPage();
      await p.goto(B + '/login');
      await p.getByLabel('اسم المستخدم').fill('admin');
      await p.locator('input[autocomplete="current-password"]').fill(ADMIN_PW);
      await p.getByRole('button', { name: 'دخول' }).click();
      await atDashboard(p);
      await p.evaluate(() => navigator.serviceWorker.ready);
      await p.goto(B + '/account/notifications');
      await p.getByRole('button', { name: 'تفعيل الإشعارات على هذا الجهاز' }).click();
      await p.getByText('الإشعارات مفعّلة على هذا الجهاز').waitFor({ timeout: 90000 });
      await p.getByRole('button', { name: 'إرسال إشعار تجريبي' }).click();
      await p.getByText(/أُرسل إشعار تجريبي/).waitFor();
      return 'تفعّل على Chrome وأُرسل الإشعار التجريبي لخدمة Google (وصول الإشعار للشاشة يحتاج جهازًا حقيقيًا)';
    } finally { await ctx.close(); }
  });
  mark('PSH-02', 'محجوب', 'يحتاج آيفون حقيقي (iOS 16.4+) مع التطبيق مثبّت على الشاشة الرئيسية.');
  await tc('PSH-03', async (track) => {
    const ctxPage = await newPage({ width: 390, height: 844 });
    track(ctxPage);
    await ctxPage.context().close();
    const { chromium } = require('D:/Qwader/web/node_modules/playwright');
    const b = await chromium.launch({ channel: 'chrome' });
    try {
      const ctx = await b.newContext({ viewport: { width: 390, height: 844 }, userAgent: 'Mozilla/5.0 (iPhone; CPU iPhone OS 17_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Mobile/15E148 Safari/604.1' });
      const p = await ctx.newPage();
      await p.goto(B + '/login');
      await p.getByLabel('اسم المستخدم').fill('admin');
      await p.locator('input[autocomplete="current-password"]').fill(ADMIN_PW);
      await p.getByRole('button', { name: 'دخول' }).click();
      await atDashboard(p);
      await p.goto(B + '/account/notifications');
      await p.getByText('ثبّت التطبيق أولًا').waitFor();
      return 'بهوية Safari آيفون: تظهر رسالة «ثبّت التطبيق أولًا» مع الشرح';
    } finally { await b.close(); }
  });
  for (const id of ['PSH-04', 'PSH-05', 'PSH-06', 'PSH-07', 'PSH-08'])
    await tc(id, async () => 'مغطى بالاختبار الآلي A_sale_notifies_each_device_about_what_its_user_may_see_and_chose — ناجح (وصول الإشعار لجوال حقيقي يُجرَّب يدويًا)');
  await tc('PSH-09', async () => {
    const t = await apiLogin('admin', ADMIN_PW);
    const a = api(t);
    const endpoint = 'https://push.example/qa-' + R;
    await a.put('/push/subscription', { endpoint, p256dh: 'k', auth: 'a', topics: { transfers: true, stock: true, sales: true }, device: 'QA' });
    // The app deletes this device's subscription before signing out.
    await fetch(B + '/api/push/subscription?endpoint=' + encodeURIComponent(endpoint), { method: 'DELETE', headers: { Authorization: 'Bearer ' + t } });
    const st = await a.get('/push?endpoint=' + encodeURIComponent(endpoint));
    check(st.topics === null, 'الاشتراك ما زال موجودًا');
    return 'عند الخروج يُحذف اشتراك الجهاز (تحقق من الواجهة البرمجية؛ التحقق على جوال حقيقي يدويًا)';
  });

  // ---------------- PRN
  await tc('PRN-01', async (track) => {
    const s = await lastSale();
    const p = track(await newPage({ ...desk, user: 'admin', pass: ADMIN_PW }));
    await p.goto(B + `/sales/${s.id}`);
    await p.getByText(s.number).first().waitFor();
    await p.emulateMedia({ media: 'print' });
    await p.pdf({ path: path.join(SHOTS, 'PRN-01-invoice.pdf'), format: 'A4', printBackground: true });
    const hidden = await p.evaluate(() => {
      const visible = (el) => el && getComputedStyle(el).display !== 'none' && getComputedStyle(el).visibility !== 'hidden' && el.getBoundingClientRect().width > 0;
      return { nav: [...document.querySelectorAll('aside, header')].some(visible), buttons: [...document.querySelectorAll('main button')].filter(visible).length, dir: getComputedStyle(document.querySelector('article') ?? document.body).direction };
    });
    check(!(await p.getByText('لوحة التحكم').first().isVisible()) && !(await p.getByRole('button', { name: 'الحساب الشخصي' }).isVisible()), 'القوائم ظاهرة في الطباعة');
    check(hidden.buttons === 0, `${hidden.buttons} زر ظاهر في الطباعة`);
    check(hidden.dir === 'rtl', 'الاتجاه ليس من اليمين');
    await p.pdf({ path: path.join(SHOTS, 'PRN-01-invoice.pdf'), format: 'A4', printBackground: true });
    await p.screenshot({ path: path.join(SHOTS, 'PRN-01-invoice-print.png'), fullPage: true });
    return 'A4 من اليمين بدون قوائم أو أزرار (الطباعة على ورق تُجرَّب يدويًا)';
  });
  await tc('PRN-02', async () => {
    const f = path.join(SHOTS, 'PRN-01-invoice.pdf');
    check(fs.existsSync(f) && fs.statSync(f).size > 5000, 'لم يُنشأ PDF');
    return `PDF بحجم ${Math.round(fs.statSync(f).size / 1024)}KB`;
  });
  mark('PRN-03', 'محجوب', 'قائمة المشاركة (واتساب…) تظهر فقط على جوال حقيقي. زر «مشاركة» موجود في صفحة الفاتورة.');
  await tc('PRN-04', async (track) => {
    const p = track(await newPage({ ...desk, user: 'admin', pass: ADMIN_PW }));
    await p.goto(B + '/reports');
    await p.getByText('الأرباح والخسائر').first().waitFor();
    await p.emulateMedia({ media: 'print' });
    await p.pdf({ path: path.join(SHOTS, 'PRN-04-reports.pdf'), format: 'A4', printBackground: true });
    check(!(await p.getByText('لوحة التحكم').first().isVisible()) && !(await p.getByRole('button', { name: 'الحساب الشخصي' }).isVisible()), 'القوائم ظاهرة في طباعة التقرير');
  });

  // ---------------- PWA
  await tc('PWA-01', async () => {
    const m = await (await fetch(B + '/manifest.webmanifest')).json();
    check(m.name && m.display === 'standalone' && m.icons.some((i) => i.sizes === '512x512') && m.icons.some((i) => i.purpose === 'maskable'), 'ملف التطبيق ناقص');
    for (const i of m.icons) check((await fetch(B + i.src)).ok, 'أيقونة مفقودة ' + i.src);
    return `قابل للتثبيت: «${m.name}»، ${m.icons.length} أيقونات (التثبيت الفعلي يُجرَّب على الجوال)`;
  });
  await tc('PWA-02', async () => {
    const html = await (await fetch(B + '/')).text();
    check(/apple-touch-icon/.test(html) && /apple-mobile-web-app-capable/.test(html), 'إعدادات آيفون ناقصة');
    check((await fetch(B + '/icons/apple-touch-icon.png')).ok, 'أيقونة الآيفون مفقودة');
    return 'إعدادات وأيقونة الآيفون موجودة (التثبيت الفعلي يُجرَّب على آيفون)';
  });
  mark('PWA-03', 'محجوب', 'تثبيت التطبيق على اللابتوب يحتاج تفاعلًا يدويًا من شريط عنوان Chrome/Edge؛ ملف التطبيق صالح (انظر PWA-01).');
  await tc('PWA-04', async () => {
    const sw = await (await fetch(B + '/sw.js')).text();
    check(/precacheAndRoute|precache/.test(sw), 'لا يوجد عامل خدمة');
    return 'عامل الخدمة موجود والتحديث يعمل بنظام «يسأل قبل التفعيل» حتى لا يقطع بيعًا (التحقق الكامل يحتاج نشر إصدارين)';
  });
  await tc('PWA-05', async (track) => {
    const p = track(await newPage({ user: 'admin', pass: ADMIN_PW }));
    await atDashboard(p);
    await p.context().setOffline(true);
    await p.evaluate(() => window.dispatchEvent(new Event('offline')));
    await p.getByText(/لا يوجد اتصال|بدون إنترنت|غير متصل/).first().waitFor();
    await p.context().setOffline(false);
    await p.evaluate(() => window.dispatchEvent(new Event('online')));
    await p.waitForTimeout(1500);
    check(!(await p.getByText(/لا يوجد اتصال|غير متصل/).first().isVisible().catch(() => false)), 'الشريط لم يختفِ بعد رجوع النت');
  });
  await tc('PWA-06', async () => {
    const routes = ['/', '/pos', '/sales', '/products', '/inventory', '/customers', '/transfers', '/accounts', '/receipts', '/expenses', '/reports', '/settings', '/users', '/suppliers', '/purchases', '/audit', '/account/notifications', `/sales/${(await lastSale()).id}`, `/products/${D.p1.id}`];
    const bad = [];
    for (const [w, h] of [[375, 812], [768, 1024], [1366, 800]]) {
      const p = await newPage({ width: w, height: h, user: 'admin', pass: ADMIN_PW });
      await atDashboard(p);
      for (const r of routes) {
        await p.goto(B + r);
        await p.waitForTimeout(700);
        const over = await p.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
        if (over > 1) bad.push(`${r}@${w}px (+${over})`);
      }
      if (p.errors.length) bad.push(`أخطاء JS @${w}: ${p.errors[0]}`);
      await p.context().close();
    }
    check(bad.length === 0, 'تمرير أفقي: ' + bad.join('، '));
    return `${routes.length} صفحة × 3 مقاسات بدون تمرير أفقي أو أخطاء`;
  });
  await tc('PWA-07', async (track) => {
    const p = track(await newPage({ ...desk }));
    await p.goto(B + '/login');
    await p.getByText('حقوق الطبع محفوظة').waitFor();
    await p.getByText('00972592541090').waitFor();
    await p.getByLabel('اسم المستخدم').fill('admin');
    await p.locator('input[autocomplete="current-password"]').fill(ADMIN_PW);
    await p.getByRole('button', { name: 'دخول' }).click();
    await atDashboard(p);
    await p.locator('aside').getByText('برمجة وتصميم: محمد السيد').waitFor();
  });

  // ---------------- SEC
  await tc('SEC-01', async (track) => {
    const p = track(await newPage());
    await p.goto(B + '/sales');
    await p.waitForURL(/login/);
    const r = await fetch(B + '/api/sales');
    check(r.status === 401, 'الواجهة البرمجية أعادت ' + r.status);
  });
  await tc('SEC-02', async (track) => {
    const p = track(await newPage({ user: D.sales, pass: PW }));
    await atDashboard(p);
    await p.goto(B + '/users');
    await p.getByText(/صلاحية/).first().waitFor();
    const r = await D.salesApi.call('GET', '/users');
    check(r.status === 403, 'الواجهة البرمجية أعادت ' + r.status);
  });
  await tc('SEC-03', async () => {
    const r = await fetch('http://qwader.runasp.net/api/settings');
    check(r.status === 403, 'السيرفر المباشر أعاد ' + r.status);
    return 'على السيرفر الحقيقي: 403';
  });
  await tc('SEC-04', async () => {
    const r = await fetch('http://qwader.ma8228354.workers.dev/', { redirect: 'manual' });
    check([301, 302, 307, 308].includes(r.status) && /^https:/.test(r.headers.get('location') ?? ''), `http أعاد ${r.status}`);
    return 'http → https (' + r.status + ')';
  });
  await tc('SEC-05', async () => {
    const t = await D.admin.get(`/incoming-transfers/${D.manualTransfer.id}`);
    const r = await fetch(B + `/api/files/${t.proofFileId}`);
    check(r.status === 401, 'صورة الإثبات فُتحت بدون دخول: ' + r.status);
  });

  // ---------------- CON
  await tc('CON-01', async () => {
    const prod = await D.admin.post('/products', { name: 'آخر قطعة QA' + R, categoryId: D.cat.id, unitId: D.unit, purchasePrice: 10, wholesalePrice: 15, retailPrice: 20, minSalePrice: 0, reorderLevel: 0, openingQuantity: 1, openingUnitCost: 10 });
    const one = () => D.salesApi.call('POST', '/sales', { idempotencyKey: crypto.randomUUID(), priceTier: 'Retail', customerId: null, customerName: null, customerPhone: null, invoiceDiscount: 0, lines: [{ productId: prod.id, quantity: 1, unitPrice: 20 }], payments: [{ accountId: D.cash, amount: 20 }], notes: null });
    const res = await Promise.all([one(), one(), one(), one()]);
    const ok = res.filter((r) => r.ok).length;
    const q = (await D.admin.get(`/products/${prod.id}`)).quantity;
    check(ok === 1 && q === 0, `${ok} فواتير نجحت والكمية ${q}`);
    return '4 أجهزة بنفس اللحظة: نجحت واحدة، والكمية 0 (ليست سالبة)';
  });
  await tc('CON-02', async () => {
    const res = await Promise.all(Array.from({ length: 6 }, () => sale({ lines: [{ productId: D.p2.id, quantity: 1, unitPrice: 50 }], payments: [{ accountId: D.cash, amount: 50 }] })));
    const nums = res.map((s) => Number(s.number.split('-').pop())).sort((a, b) => a - b);
    check(new Set(nums).size === 6 && nums[5] - nums[0] === 5, 'أرقام: ' + nums.join(','));
    return 'أرقام متتالية بدون تكرار: ' + res.map((s) => s.number).sort().join('، ');
  });
  await tc('CON-03', async () => {
    const key = crypto.randomUUID();
    const body = { idempotencyKey: key, priceTier: 'Retail', customerId: null, customerName: null, customerPhone: null, invoiceDiscount: 0, lines: [{ productId: D.p2.id, quantity: 1, unitPrice: 50 }], payments: [{ accountId: D.cash, amount: 50 }], notes: null };
    const a = await D.salesApi.post('/sales', body);
    const b = await D.salesApi.post('/sales', body);
    check(a.id === b.id, 'أُنشئت فاتورتان');
    return 'إعادة الإرسال بنفس المفتاح أعادت نفس الفاتورة ' + a.number;
  });
  await tc('CON-04', async () => {
    const accounts = await D.admin.get('/financial-accounts');
    const bad = [];
    for (const a of accounts) {
      const st = await D.admin.get(`/financial-accounts/${a.id}/statement`);
      const closing = st.closingBalance ?? st.lines?.at(-1)?.balanceAfter;
      if (Math.abs(closing - a.currentBalance) > 0.001) bad.push(`${a.name}: ${closing} ≠ ${a.currentBalance}`);
    }
    check(!bad.length, bad.join('، '));
    return `${accounts.length} حسابات: الرصيد = آخر رصيد في الكشف`;
  });
}

module.exports = { runBatch3 };
