// Executes the manual test-case sheet against a local QA copy (same build as production, separate database).
// Writes results.json: { "AUTH-01": { status, note }, ... }
const fs = require('fs');
const path = require('path');
const { chromium } = require('D:/Qwader/web/node_modules/playwright');

const B = 'http://localhost:5298';
const OUT = __dirname;
const SHOTS = path.join(OUT, 'shots-final');
fs.mkdirSync(SHOTS, { recursive: true });
const results = fs.existsSync(path.join(OUT, 'results.json')) && process.argv.includes('--keep') ? JSON.parse(fs.readFileSync(path.join(OUT, 'results.json'), 'utf8')) : {};
const only = process.argv.find((a) => a.startsWith('--only='))?.slice(7).split(',');
const save = () => fs.writeFileSync(path.join(OUT, 'results.json'), JSON.stringify(results, null, 1));
const R = String(Date.now()).slice(-6);
const ADMIN_PW = 'Admin12345';
const PW = 'Changed123';

function check(cond, msg) { if (!cond) throw new Error(msg); }

// ---------------------------------------------------------------- API helpers (setup and verification)
async function apiLogin(user, pass) {
  const r = await fetch(B + '/api/auth/login', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ userName: user, password: pass }) });
  if (!r.ok) throw new Error(`login ${user}: ${r.status} ${await r.text()}`);
  return (await r.json()).accessToken;
}
// A client that signs in again when its 15-minute access token expires.
function apiAs(user, pass) {
  let token = null;
  const c = api(async (renew) => (token && !renew ? token : (token = await apiLogin(user, pass))));
  return c;
}
function api(tokenOrProvider) {
  const getToken = typeof tokenOrProvider === 'function' ? tokenOrProvider : async () => tokenOrProvider;
  const call = async (method, p, body, retried) => {
    const r = await fetch(B + '/api' + p, { method, headers: { 'Content-Type': 'application/json', Authorization: 'Bearer ' + (await getToken(retried)) }, body: body === undefined ? undefined : JSON.stringify(body) });
    if (r.status === 401 && !retried && typeof tokenOrProvider === 'function') return call(method, p, body, true);
    const text = await r.text();
    let data = null;
    try { data = text ? JSON.parse(text) : null; } catch { data = text; }
    return { status: r.status, ok: r.ok, data };
  };
  const must = async (method, p, body) => {
    const res = await call(method, p, body);
    if (!res.ok) throw new Error(`${method} ${p} → ${res.status} ${JSON.stringify(res.data).slice(0, 200)}`);
    return res.data;
  };
  return { call, get: (p) => must('GET', p), post: (p, b) => must('POST', p, b ?? {}), put: (p, b) => must('PUT', p, b) };
}

// ---------------------------------------------------------------- browser helpers
let browser;
async function newPage({ width = 390, height = 844, user, pass } = {}) {
  const ctx = await browser.newContext({ viewport: { width, height }, locale: 'ar' });
  const page = await ctx.newPage();
  page.errors = [];
  page.on('pageerror', (e) => page.errors.push(e.message));
  if (user) {
    await uiLogin(page, user, pass);
    await page.waitForURL((u) => !u.pathname.startsWith('/login'), { timeout: 20000 });
    await page.getByRole('button', { name: 'الحساب الشخصي' }).or(page.getByText('تغيير كلمة المرور')).first().waitFor();
  }
  return page;
}
async function uiLogin(page, user, pass) {
  await page.goto(B + '/login');
  await page.getByLabel('اسم المستخدم').fill(user);
  await page.locator('input[autocomplete="current-password"]').fill(pass);
  await page.getByRole('button', { name: 'دخول' }).click();
}
async function atDashboard(page) { await page.getByText('إجراءات سريعة').waitFor({ timeout: 15000 }); }

let current = null;
async function tc(id, fn) {
  if (only && !only.some((o) => id.startsWith(o))) return;
  current = id;
  const pages = [];
  try {
    const note = await fn((p) => (pages.push(p), p));
    results[id] = { status: 'نجح', note: note || '' };
    console.log('PASS', id, note || '');
  } catch (e) {
    const msg = String(e.message || e).split('\n')[0].slice(0, 300);
    results[id] = { status: 'فشل', note: msg };
    console.log('FAIL', id, msg);
    for (const [i, p] of pages.entries()) await p.screenshot({ path: path.join(SHOTS, `${id}-${i}.png`) }).catch(() => {});
  } finally {
    for (const p of pages) await p.context().close().catch(() => {});
    save();
  }
}
function mark(id, status, note) {
  if (only && !only.some((o) => id.startsWith(o))) return;
  results[id] = { status, note };
  console.log(status === 'محجوب' ? 'BLOCK' : 'INFO', id, note);
  save();
}

// ---------------------------------------------------------------- data
const D = {};
async function setup() {
  const admin = apiAs('admin', ADMIN_PW);
  D.admin = admin;
  const lookups = await admin.get('/lookups');
  D.cash = lookups.accounts.find((a) => a.type === 'Cash').id;
  D.jawwal = lookups.accounts.find((a) => a.name === 'جوال بي').id;
  D.bank = lookups.accounts.find((a) => a.name === 'بنك فلسطين').id;
  D.unit = lookups.units[0].id;
  // Money in the till so payments and refunds have funds.
  const cashAcc = await admin.get(`/financial-accounts/${D.cash}`);
  await admin.post(`/financial-accounts/${D.cash}/opening-balance`, { openingBalance: cashAcc.openingBalance + 10000, notes: 'QA' });
  D.cat = await admin.post('/categories', { name: 'أدوات مطبخ QA' + R, isActive: true });
  const product = (name, extra) => admin.post('/products', { name, categoryId: D.cat.id, unitId: D.unit, purchasePrice: 70, wholesalePrice: 100, retailPrice: 120, minSalePrice: 100, reorderLevel: 2, openingQuantity: 20, openingUnitCost: 70, ...extra });
  D.p1 = await product('خلاط كهربائي QA' + R, { barcode: '629' + R + '1234' });
  D.p2 = await product('غسالة أوتوماتيك QA' + R, { retailPrice: 50, wholesalePrice: 40, minSalePrice: 0, purchasePrice: 30, openingUnitCost: 30, openingQuantity: 30 });
  D.last = await product('مكواة QA' + R, { openingQuantity: 1, reorderLevel: 0 });
  // One user per role (password already changed).
  const mkUser = async (role) => {
    const userName = `${role.toLowerCase()}_${R}`;
    await admin.post('/users', { userName, fullName: `${role} QA`, phoneNumber: null, password: 'Temp12345', roles: [role] });
    const t = await apiLogin(userName, 'Temp12345');
    await api(t).post('/auth/change-password', { currentPassword: 'Temp12345', newPassword: PW });
    return userName;
  };
  D.sales = await mkUser('Sales');
  D.manager = await mkUser('Manager');
  D.acct = await mkUser('Accountant');
  D.viewer = await mkUser('Viewer');
  D.store = await mkUser('Storekeeper');
  D.salesApi = apiAs(D.sales, PW);
}

// ---------------------------------------------------------------- POS helper (UI)
async function posSell(page, { code, qty, account = 'الصندوق النقدي', customer, phone, reference, beforeConfirm } = {}) {
  await page.goto(B + '/pos');
  const search = page.getByPlaceholder(/ابحث بالاسم أو الكود/);
  await search.fill(code);
  await page.keyboard.press('Enter');
  await page.getByLabel('زيادة الكمية').first().waitFor();
  for (let i = 1; i < (qty || 1); i++) await page.getByLabel('زيادة الكمية').first().click();
  await page.getByRole('button', { name: 'الدفع' }).last().click();
  const co = page.getByRole('dialog', { name: 'إتمام البيع' });
  await co.waitFor();
  if (customer) {
    await co.getByRole('button', { name: 'عميل (اسم وجوال)' }).click();
    await co.getByLabel('اسم العميل').fill(customer);
    await co.getByLabel('رقم الجوال').fill(phone);
  }
  await co.getByRole('radio', { name: account }).click();
  if (reference) await co.getByLabel(/رقم مرجع الحوالة/).fill(reference);
  if (beforeConfirm) await beforeConfirm(co);
  await co.getByRole('button', { name: 'اعتماد الفاتورة' }).click();
  return co;
}
/** Switches checkout to partial / multi-account payment with one cash row of the given amount. */
async function partial(co, amount) {
  await co.getByRole('button', { name: 'دفع جزئي (آجل) أو بأكثر من حساب' }).click();
  await co.getByLabel('الحساب').first().selectOption({ label: 'الصندوق النقدي' });
  await co.getByLabel('المبلغ').first().fill(String(amount));
}
async function lastSale(apiClient = D.admin) {
  return (await apiClient.get('/sales?pageSize=1')).items[0];
}

// ================================================================= cases
async function run() {
  browser = await chromium.launch({ channel: 'chrome' });
  await setup();
  console.log('setup done', R);

  // ---------------- AUTH
  await tc('AUTH-01', async (track) => {
    const p = track(await newPage());
    await uiLogin(p, 'admin', ADMIN_PW);
    await atDashboard(p);
    check(await p.getByText('أهلًا').count(), 'لا تظهر التحية في لوحة التحكم');
  });
  await tc('AUTH-02', async (track) => {
    const p = track(await newPage());
    await uiLogin(p, 'admin', 'wrong-pass-1');
    await p.getByRole('alert').filter({ hasText: 'اسم المستخدم أو كلمة المرور غير صحيحة' }).waitFor();
    check(p.url().includes('/login'), 'غادر صفحة الدخول');
  });
  await tc('AUTH-03', async (track) => {
    const userName = `lock_${R}`;
    await D.admin.post('/users', { userName, fullName: 'قفل', phoneNumber: null, password: 'Temp12345', roles: ['Viewer'] });
    const p = track(await newPage());
    for (let i = 0; i < 5; i++) { await uiLogin(p, userName, 'bad-pass'); await p.getByRole('alert').waitFor(); }
    await uiLogin(p, userName, 'Temp12345');
    await p.getByRole('alert').filter({ hasText: 'تم إيقاف الدخول مؤقتًا' }).waitFor();
    return 'بعد 5 محاولات خاطئة رُفضت كلمة المرور الصحيحة برسالة الإيقاف المؤقت';
  });
  await tc('AUTH-04', async (track) => {
    const userName = `new_${R}`;
    await D.admin.post('/users', { userName, fullName: 'جديد', phoneNumber: null, password: 'Temp12345', roles: ['Sales'] });
    const p = track(await newPage());
    await uiLogin(p, userName, 'Temp12345');
    await p.waitForURL(/account\/password/);
    await p.goto(B + '/sales');
    await p.waitForURL(/account\/password/);
    D.newUser = userName;
  });
  await tc('AUTH-05', async (track) => {
    const p = track(await newPage({ user: D.viewer, pass: PW }));
    await atDashboard(p);
    // Through the account menu, as a user does (a direct URL once hid a broken menu link).
    await p.getByRole('button', { name: 'الحساب الشخصي' }).click();
    await p.getByRole('button', { name: 'تغيير كلمة المرور' }).click();
    await p.getByLabel('كلمة المرور الحالية').fill('not-my-password');
    await p.getByLabel(/^كلمة المرور الجديدة/).fill('Another123');
    await p.getByLabel('تأكيد كلمة المرور الجديدة').fill('Another123');
    await p.getByRole('button', { name: /حفظ|تغيير/ }).last().click();
    await p.getByText('كلمة المرور الحالية غير صحيحة').waitFor();
  });
  await tc('AUTH-06', async (track) => {
    const p = track(await newPage());
    await uiLogin(p, D.newUser, 'Temp12345');
    await p.waitForURL(/account\/password/);
    await p.getByLabel('كلمة المرور الحالية').fill('Temp12345');
    await p.getByLabel(/^كلمة المرور الجديدة/).fill('Brand123');
    await p.getByLabel('تأكيد كلمة المرور الجديدة').fill('Brand123');
    await p.getByRole('button', { name: /حفظ|تغيير/ }).last().click();
    await atDashboard(p);
    const old = await fetch(B + '/api/auth/login', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ userName: D.newUser, password: 'Temp12345' }) });
    check(old.status === 401, 'كلمة المرور القديمة ما زالت تعمل');
    await apiLogin(D.newUser, 'Brand123');
  });
  await tc('AUTH-07', async (track) => {
    const p = track(await newPage({ user: 'admin', pass: ADMIN_PW }));
    await atDashboard(p);
    const p2 = await p.context().newPage();
    await p.close();
    await p2.goto(B + '/');
    await atDashboard(p2);
    return 'فُتح التطبيق من جديد بدون طلب دخول';
  });
  await tc('AUTH-08', async (track) => {
    const p = track(await newPage({ user: 'admin', pass: ADMIN_PW }));
    await atDashboard(p);
    await p.getByRole('button', { name: 'الحساب الشخصي' }).click();
    await p.getByRole('button', { name: 'تسجيل الخروج' }).click();
    await p.waitForURL(/login/);
    await p.goto(B + '/sales');
    await p.waitForURL(/login/);
  });
  await tc('AUTH-09', async (track) => {
    const userName = `off_${R}`;
    const u = await D.admin.post('/users', { userName, fullName: 'معطل', phoneNumber: null, password: 'Temp12345', roles: ['Sales'] });
    await D.admin.put(`/users/${u.id}`, { fullName: 'معطل', phoneNumber: null, isActive: false, roles: ['Sales'] });
    const p = track(await newPage());
    await uiLogin(p, userName, 'Temp12345');
    await p.getByRole('alert').filter({ hasText: 'معطّل' }).waitFor();
  });
  await tc('AUTH-10', async (track) => {
    const p = track(await newPage());
    await p.goto(B + '/login');
    const input = p.locator('input[autocomplete="current-password"]');
    await input.fill('secret');
    await p.getByRole('button', { name: 'إظهار كلمة المرور' }).click();
    check((await input.getAttribute('type')) === 'text', 'لم تظهر كلمة المرور');
    await p.getByRole('button', { name: 'إخفاء كلمة المرور' }).click();
    check((await input.getAttribute('type')) === 'password', 'لم تختفِ كلمة المرور');
  });

  // ---------------- USR
  await tc('USR-01', async (track) => {
    const p = track(await newPage({ width: 1366, height: 800, user: 'admin', pass: ADMIN_PW }));
    await p.goto(B + '/users');
    await p.getByRole('button', { name: 'مستخدم جديد' }).click();
    const sheet = p.getByRole('dialog', { name: 'مستخدم جديد' });
    const userName = `ui_${R}`;
    await sheet.getByLabel('الاسم الكامل').fill('موظف من الشاشة');
    await sheet.getByLabel('اسم المستخدم (للدخول)').fill(userName);
    await sheet.getByLabel('كلمة المرور المؤقتة').fill('Temp12345');
    await sheet.getByText('موظف مبيعات', { exact: true }).click();
    await sheet.getByRole('button', { name: 'إضافة المستخدم' }).click();
    await sheet.waitFor({ state: 'hidden' });
    await p.getByText('موظف من الشاشة').waitFor();
    await apiLogin(userName, 'Temp12345');
    D.uiUser = userName;
  });
  await tc('USR-02', async (track) => {
    const p = track(await newPage({ width: 1366, height: 800, user: 'admin', pass: ADMIN_PW }));
    await p.goto(B + '/users');
    await p.getByRole('button', { name: 'مستخدم جديد' }).click();
    const sheet = p.getByRole('dialog', { name: 'مستخدم جديد' });
    await sheet.getByLabel('الاسم الكامل').fill('مكرر');
    await sheet.getByLabel('اسم المستخدم (للدخول)').fill(D.sales);
    await sheet.getByLabel('كلمة المرور المؤقتة').fill('Temp12345');
    await sheet.getByText('موظف مبيعات', { exact: true }).click();
    await sheet.getByRole('button', { name: 'إضافة المستخدم' }).click();
    await sheet.getByText('اسم المستخدم مستخدم من قبل').waitFor();
  });
  await tc('USR-03', async () => {
    const me = (await D.admin.get('/users?search=admin')).items.find((u) => u.userName === 'admin');
    const res = await D.admin.call('PUT', `/users/${me.id}`, { fullName: me.fullName, phoneNumber: me.phoneNumber, isActive: false, roles: me.roles.map((r) => r.name) });
    check(!res.ok, 'سُمح للمدير بتعطيل نفسه');
    return `رُفض (${res.status}): ${JSON.stringify(res.data).match(/"[^"]*[ء-ي][^"]*"/)?.[0] ?? ''}`;
  });
  await tc('USR-04', async (track) => {
    const p = track(await newPage({ width: 1366, height: 800, user: 'admin', pass: ADMIN_PW }));
    await p.goto(B + '/users');
    await p.getByText('موظف من الشاشة').click();
    await p.getByRole('button', { name: 'تعيين كلمة مرور جديدة' }).click();
    const sheet = p.getByRole('dialog', { name: /كلمة مرور جديدة/ });
    await sheet.getByLabel('كلمة المرور المؤقتة').fill('Reset12345');
    await sheet.getByRole('button', { name: 'تعيين' }).click();
    await sheet.waitFor({ state: 'hidden' });
    const login = await fetch(B + '/api/auth/login', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ userName: D.uiUser, password: 'Reset12345' }) });
    check(login.ok, 'لا يمكن الدخول بكلمة المرور الجديدة');
    check((await login.json()).user.mustChangePassword, 'لم يُطلب تغيير كلمة المرور');
  });
  await tc('USR-05', async (track) => {
    const role = await D.admin.post('/roles', { displayName: 'دور QA ' + R, description: null, permissions: ['Sales.Create'] });
    const userName = `role_${R}`;
    await D.admin.post('/users', { userName, fullName: 'دور مخصص', phoneNumber: null, password: 'Temp12345', roles: [role.name] });
    const t = await apiLogin(userName, 'Temp12345');
    await api(t).post('/auth/change-password', { currentPassword: 'Temp12345', newPassword: PW });
    const p = track(await newPage({ width: 1366, height: 800, user: 'admin', pass: ADMIN_PW }));
    await p.goto(B + '/users');
    await p.getByRole('tab', { name: 'الأدوار والصلاحيات' }).click();
    await p.getByText('دور QA ' + R).click();
    await p.getByText('عرض فواتير جميع الموظفين').click();
    await p.getByRole('button', { name: /حفظ/ }).last().click();
    await p.waitForTimeout(800);
    const s = track(await newPage({ width: 1366, height: 800, user: userName, pass: PW }));
    await s.goto(B + '/sales');
    await s.getByRole('heading', { name: 'فواتير المبيعات' }).waitFor();
    check(!(await s.getByText('فواتيرك أنت فقط').count()), 'ما زال يرى «فواتيرك أنت فقط» بعد إضافة الصلاحية');
  });
  await tc('USR-06', async () => {
    const roles = await D.admin.get('/roles');
    const adminRole = roles.find((r) => r.name === 'Admin');
    const res = await D.admin.call('PUT', `/roles/${adminRole.id}`, { displayName: adminRole.displayName, description: adminRole.description, permissions: adminRole.permissions.slice(1) });
    check(!res.ok, 'تم إنقاص صلاحيات مدير النظام');
    return `رُفض (${res.status})`;
  });
  await tc('USR-07', async (track) => {
    const p = track(await newPage({ user: D.sales, pass: PW }));
    await atDashboard(p);
    await p.getByRole('button', { name: 'المزيد' }).click();
    const sheet = p.getByRole('dialog', { name: 'كل الأقسام' });
    await sheet.waitFor();
    check(!(await sheet.getByText('المستخدمون').count()) && !(await sheet.getByText('الإعدادات', { exact: true }).count()), 'تظهر المستخدمون أو الإعدادات لموظف المبيعات');
    await p.goto(B + '/users');
    await p.getByText(/لا تملك|ليست لديك|صلاحية/).first().waitFor();
  });
  await tc('USR-08', async (track) => {
    const p = track(await newPage({ user: D.sales, pass: PW }));
    await atDashboard(p);
    await p.goto(B + `/products/${D.p1.id}`);
    await p.getByText(D.p1.name).first().waitFor();
    const text = await p.locator('main').innerText();
    check(!/سعر الشراء|متوسط التكلفة|الربح المتوقع/.test(text), 'تظهر التكلفة لموظف المبيعات');
  });
  await tc('USR-09', async (track) => {
    const p = track(await newPage({ user: D.sales, pass: PW }));
    await atDashboard(p);
    await p.goto(B + '/pos');
    await p.getByPlaceholder(/ابحث بالاسم أو الكود/).waitFor();
    check(!(await p.getByRole('radio', { name: 'جملة' }).count()) && !(await p.getByText('جملة', { exact: true }).count()), 'خيار الجملة ظاهر لموظف المبيعات');
    const m = track(await newPage({ user: D.manager, pass: PW }));
    await atDashboard(m);
    await m.goto(B + '/pos');
    await m.getByText('جملة', { exact: true }).first().waitFor();
  });
  await tc('USR-10', async (track) => {
    const p = track(await newPage({ user: D.viewer, pass: PW }));
    await atDashboard(p);
    const v = api(await apiLogin(D.viewer, PW));
    const sale = await v.call('POST', '/sales', { idempotencyKey: crypto.randomUUID(), priceTier: 'Retail', customerId: null, customerName: null, customerPhone: null, invoiceDiscount: 0, lines: [{ productId: D.p2.id, quantity: 1, unitPrice: 50 }], payments: [{ accountId: D.cash, amount: 50 }], notes: null });
    const prod = await v.call('POST', '/products', { name: 'x', categoryId: D.cat.id, unitId: D.unit, wholesalePrice: 1, retailPrice: 1, minSalePrice: 0, reorderLevel: 0 });
    check(sale.status === 403 && prod.status === 403, `المشاهد استطاع الإنشاء (${sale.status}/${prod.status})`);
    check(!(await p.getByRole('link', { name: 'بيع جديد' }).count()) && !(await p.getByRole('button', { name: 'بيع جديد' }).count()), 'زر «بيع جديد» ظاهر للمشاهد');
  });

  // ---------------- SET
  await tc('SET-01', async (track) => {
    const p = track(await newPage({ width: 1366, height: 800, user: 'admin', pass: ADMIN_PW }));
    await p.goto(B + '/settings');
    await p.getByLabel('رقم الهاتف').fill('0599' + R);
    await p.getByLabel('نص أسفل الفاتورة').fill('شكرًا لزيارتكم QA' + R);
    await p.getByRole('button', { name: 'حفظ الإعدادات' }).click();
    await p.waitForTimeout(800);
    await D.admin.post('/sales', { idempotencyKey: crypto.randomUUID(), priceTier: 'Retail', customerId: null, customerName: null, customerPhone: null, invoiceDiscount: 0, lines: [{ productId: D.p2.id, quantity: 1, unitPrice: 50 }], payments: [{ accountId: D.cash, amount: 50 }], notes: null });
    const s = await lastSale();
    await p.goto(B + `/sales/${s.id}`);
    await p.getByText(s.number).first().waitFor();
    await p.emulateMedia({ media: 'print' }); // the store details are printed on the invoice
    await p.getByText('شكرًا لزيارتكم QA' + R).waitFor();
    await p.getByText('0599' + R).first().waitFor();
  });
  await tc('SET-02', async (track) => {
    const st = await D.admin.get('/settings');
    await D.admin.put('/settings', { ...st, maxDiscountPercent: 10 });
    const p = track(await newPage({ user: D.sales, pass: PW }));
    await atDashboard(p);
    const co = await posSell(p, { code: D.p1.sku, beforeConfirm: async (co) => { await co.getByLabel('خصم على الفاتورة (₪)').fill('15'); } });
    await co.getByRole('alert').waitFor();
    const msg = await co.getByRole('alert').innerText();
    check(/خصم|10/.test(msg), 'رسالة غير واضحة: ' + msg);
    return 'رُفض خصم 12.5%: ' + msg.slice(0, 80);
  });
  await tc('SET-03', async (track) => {
    const p = track(await newPage({ user: 'admin', pass: ADMIN_PW }));
    await atDashboard(p);
    const co = await posSell(p, { code: D.p2.sku, account: 'جوال بي', customer: 'زبون إعداد QA', phone: '0595' + R, reference: 'S3' + R });
    await p.getByRole('dialog', { name: 'تم البيع' }).waitFor();
    const s = await lastSale();
    check(s.paidAmount === 0, 'الفاتورة اعتُبرت مدفوعة');
    const t = (await D.admin.get('/incoming-transfers?search=S3' + R)).items[0];
    check(t && t.status === 'Pending', 'لم تُنشأ حوالة بانتظار التحقق');
    D.pendingFromSale = { sale: s, transfer: t };
    return `${s.number} آجل + ${t.number} بانتظار التحقق`;
  });
  await tc('SET-04', async (track) => {
    const p = track(await newPage({ width: 1366, height: 800, user: 'admin', pass: ADMIN_PW }));
    await p.goto(B + '/settings');
    await p.getByRole('radio', { name: /مؤكدة مباشرة/ }).click();
    await p.getByRole('button', { name: 'حفظ الإعدادات' }).click();
    await p.waitForTimeout(800);
    const before = (await D.admin.get(`/financial-accounts/${D.bank}`)).currentBalance;
    await posSell(p, { code: D.p2.sku, account: 'بنك فلسطين', reference: 'S4' + R });
    await p.getByRole('dialog', { name: 'تم البيع' }).waitFor();
    const s = await lastSale();
    const t = (await D.admin.get('/incoming-transfers?search=S4' + R)).items[0];
    const after = (await D.admin.get(`/financial-accounts/${D.bank}`)).currentBalance;
    check(s.paidAmount === s.total, 'الفاتورة غير مدفوعة');
    check(t && t.status === 'Confirmed', 'الحوالة ليست مؤكدة');
    check(after - before === 50, `رصيد البنك تغير بـ ${after - before}`);
    const st = await D.admin.get('/settings');
    await D.admin.put('/settings', { ...st, saleTransfersPending: true });
  });
  await tc('SET-05', async (track) => {
    await D.admin.put(`/products/${D.p2.id}`, { ...(await prodRequest(D.p2.id)), retailPrice: 55 });
    const p = track(await newPage({ width: 1366, height: 800, user: 'admin', pass: ADMIN_PW }));
    await p.goto(B + '/audit');
    await p.getByText(/تعديل.*منتج|تعديل أسعار/).first().waitFor();
    await D.admin.put(`/products/${D.p2.id}`, { ...(await prodRequest(D.p2.id)), retailPrice: 50 });
  });
  await tc('SET-06', async (track) => {
    const p = track(await newPage({ width: 1366, height: 800, user: D.manager, pass: PW }));
    await atDashboard(p);
    await p.goto(B + '/settings');
    await p.waitForTimeout(1500);
    check(!(await p.getByRole('button', { name: 'تصفير النظام…' }).count()), 'زر التصفير ظاهر لمدير المعرض');
  });
  await tc('SET-07', async (track) => {
    const p = track(await newPage({ user: 'admin', pass: ADMIN_PW }));
    await atDashboard(p);
    await p.goto(B + '/settings');
    await p.getByRole('button', { name: 'تصفير النظام…' }).click();
    const sheet = p.getByRole('dialog', { name: 'تصفير النظام' });
    await sheet.getByLabel(/اكتب كلمة/).fill('تصفير');
    await sheet.getByLabel('كلمة المرور الخاصة بك').fill('wrong-password');
    await sheet.getByRole('button', { name: 'مسح الحركات نهائيًا' }).click();
    await sheet.getByText('كلمة المرور غير صحيحة').waitFor();
    check((await D.admin.get('/sales?pageSize=1')).totalCount > 0, 'اختفت البيانات!');
  });
  // SET-08 / SET-09 run at the very end (they wipe data).

  await runBatch2();
  await runBatch3();

  // ---------------- destructive, last
  await tc('SET-08', async (track) => {
    const p = track(await newPage({ user: 'admin', pass: ADMIN_PW }));
    await atDashboard(p);
    await p.goto(B + '/settings');
    await p.getByRole('button', { name: 'تصفير النظام…' }).click();
    const sheet = p.getByRole('dialog', { name: 'تصفير النظام' });
    await sheet.getByLabel(/اكتب كلمة/).fill('تصفير');
    await sheet.getByLabel('كلمة المرور الخاصة بك').fill(ADMIN_PW);
    await sheet.getByRole('button', { name: 'مسح الحركات نهائيًا' }).click();
    await atDashboard(p);
    const sales = await D.admin.get('/sales?pageSize=1');
    const prod = await D.admin.get(`/products/${D.p1.id}`);
    check(sales.totalCount === 0, 'بقيت فواتير');
    check(prod.quantity === 0, 'الكمية لم تصبح صفرًا');
    return 'لا فواتير، المنتجات باقية بكمية 0';
  });
  await tc('SET-09', async (track) => {
    const p = track(await newPage({ user: 'admin', pass: ADMIN_PW }));
    await atDashboard(p);
    await p.goto(B + '/settings');
    await p.getByRole('button', { name: 'تصفير النظام…' }).click();
    const sheet = p.getByRole('dialog', { name: 'تصفير النظام' });
    await sheet.getByRole('radio', { name: /مسح كل شيء/ }).click();
    await sheet.getByLabel(/اكتب كلمة/).fill('تصفير');
    await sheet.getByLabel('كلمة المرور الخاصة بك').fill(ADMIN_PW);
    await sheet.getByRole('button', { name: 'مسح كل شيء نهائيًا' }).click();
    await atDashboard(p);
    const products = await D.admin.get('/products?pageSize=1');
    const lookups = await D.admin.get('/lookups');
    check(products.totalCount === 0, 'بقيت منتجات');
    check(lookups.accounts.some((a) => a.name === 'الصندوق النقدي'), 'الحسابات الافتراضية لم تعد');
    const first = await D.admin.post('/products', { name: 'أول منتج', categoryId: (await D.admin.post('/categories', { name: 'صنف', isActive: true })).id, unitId: lookups.units[0].id, wholesalePrice: 1, retailPrice: 1, minSalePrice: 0, reorderLevel: 0 });
    check(first.sku === 'P-00001', 'أول منتج ليس P-00001: ' + first.sku);
    return 'بقي المستخدمون والإعدادات، وعادت الحسابات، وأول منتج P-00001';
  });

  await browser.close();
}

async function prodRequest(id) {
  const p = await D.admin.get(`/products/${id}`);
  return { name: p.name, sku: p.sku, barcode: p.barcode, categoryId: p.categoryId, brandName: p.brandName, unitId: p.unitId, defaultSupplierId: null, parentProductId: null,
    size: p.size, color: p.color, model: p.model, description: p.description, purchasePrice: null, wholesalePrice: p.wholesalePrice, retailPrice: p.retailPrice,
    minSalePrice: p.minSalePrice, reorderLevel: p.reorderLevel, imageFileId: p.imageFileId };
}

module.exports = { tc, mark, check, api, apiAs, apiLogin, newPage, uiLogin, atDashboard, posSell, partial, lastSale, prodRequest, D, B, R, PW, ADMIN_PW, SHOTS };
const { runBatch2 } = require('./batch2.cjs');
const { runBatch3 } = require('./batch3.cjs');

if (require.main === module) {
  run().catch(async (e) => { console.error('RUN FAILED', current, e); save(); await browser?.close(); process.exit(1); });
}
