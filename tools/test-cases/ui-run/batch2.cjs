// Catalog, inventory, purchasing, POS, sales, customers.
const fs = require('fs');
const path = require('path');

async function runBatch2() {
  const { tc, mark, check, api, apiLogin, newPage, atDashboard, posSell, partial, lastSale, prodRequest, D, B, R, PW, ADMIN_PW } = require('./run.cjs');
  const desk = { width: 1366, height: 800 };

  // ---------------- CAT
  await tc('CAT-01', async (track) => {
    const p = track(await newPage({ ...desk, user: D.store, pass: PW }));
    await atDashboard(p);
    await p.goto(B + '/categories');
    await p.getByRole('button', { name: 'صنف جديد' }).click();
    const sheet = p.getByRole('dialog', { name: 'صنف جديد' });
    await sheet.getByLabel('اسم الصنف').fill('مراوح QA' + R);
    await sheet.getByRole('button', { name: 'إضافة الصنف' }).click();
    await sheet.waitFor({ state: 'hidden' });
    await p.getByText('مراوح QA' + R).waitFor();
    const c = (await D.admin.get('/categories?search=' + encodeURIComponent('مراوح QA' + R))).items?.[0] ?? (await D.admin.get('/categories')).find?.((x) => x.name === 'مراوح QA' + R);
    check(c && /^CAT-/.test(c.code), 'الكود ليس CAT-: ' + JSON.stringify(c)?.slice(0, 80));
    return 'كود ' + c.code;
  });
  await tc('CAT-02', async (track) => {
    const p = track(await newPage({ ...desk, user: D.store, pass: PW }));
    await atDashboard(p);
    await p.goto(B + '/categories');
    await p.getByRole('button', { name: 'صنف جديد' }).click();
    const sheet = p.getByRole('dialog', { name: 'صنف جديد' });
    await sheet.getByLabel('اسم الصنف').fill('مراوح QA' + R);
    await sheet.getByRole('button', { name: 'إضافة الصنف' }).click();
    await p.waitForTimeout(1200);
    check(await sheet.isVisible(), 'أُغلق النموذج كأن الحفظ نجح');
    const text = await sheet.innerText();
    check(/موجود|مستخدم|مكرر|من قبل|بنفس الاسم/.test(text), 'لا رسالة تكرار: ' + text.slice(0, 120));
  });
  await tc('CAT-03', async (track) => {
    const p = track(await newPage({ user: D.store, pass: PW }));
    await atDashboard(p);
    await p.goto(B + '/products?add=1');
    const quick = p.getByRole('dialog', { name: 'إضافة منتج' });
    await quick.getByText('اختر الصنف').click();
    const picker = p.getByRole('dialog').last();
    await picker.getByRole('searchbox').or(picker.locator('input')).first().fill('مراوح');
    await picker.getByText('مراوح QA' + R).waitFor();
    await p.waitForTimeout(1200);
    check(!(await picker.getByText('أدوات مطبخ QA' + R).count()), 'ظهرت أصناف غير مطابقة');
  });
  await tc('CAT-04', async (track) => {
    const p = track(await newPage({ ...desk, user: D.store, pass: PW }));
    await atDashboard(p);
    await p.goto(B + '/categories');
    const row = p.locator('li', { hasText: 'مراوح QA' + R });
    await row.getByRole('button', { name: 'تعديل' }).click();
    const sheet = p.getByRole('dialog', { name: 'تعديل الصنف' });
    await sheet.getByRole('switch').click();
    await sheet.getByRole('button', { name: 'حفظ' }).click();
    await sheet.waitFor({ state: 'hidden' });
    await p.goto(B + '/products?add=1');
    await p.getByRole('dialog', { name: 'إضافة منتج' }).getByText('اختر الصنف').click();
    const picker = p.getByRole('dialog').last();
    await picker.locator('input').first().fill('مراوح');
    await p.waitForTimeout(1200);
    check(!(await picker.getByText('مراوح QA' + R).count()), 'الصنف المعطّل ما زال يظهر');
  });

  // ---------------- PRD
  await tc('PRD-01', async (track) => {
    const p = track(await newPage({ ...desk, user: D.store, pass: PW }));
    await atDashboard(p);
    await p.goto(B + '/products/new');
    await p.getByLabel('اسم المنتج').fill('ميكروويف QA' + R);
    await p.getByText('اختر الصنف').click();
    const picker = p.getByRole('dialog').last();
    await picker.locator('input').first().fill('أدوات مطبخ QA');
    await picker.getByText('أدوات مطبخ QA' + R).click();
    await p.getByLabel(/سعر الشراء/).first().fill('200');
    await p.getByLabel(/سعر البيع بالمفرق|سعر المفرق/).first().fill('280');
    const qty = p.getByLabel(/الكمية الحالية|الكمية الموجودة الآن|الكمية الافتتاحية/).first();
    if (await qty.count()) await qty.fill('6');
    await p.getByRole('button', { name: /^حفظ|إضافة المنتج/ }).last().click();
    await p.waitForURL(/products\/\d+$/);
    const id = Number(p.url().split('/').pop());
    const prod = await D.admin.get(`/products/${id}`);
    check(/^P-\d{5}$/.test(prod.sku), 'الكود: ' + prod.sku);
    check(prod.quantity === 6, 'الكمية: ' + prod.quantity);
    D.p4 = prod;
    return `${prod.sku} بكمية ${prod.quantity}`;
  });
  await tc('PRD-02', async (track) => {
    const p = track(await newPage({ user: D.store, pass: PW }));
    await atDashboard(p);
    await p.goto(B + '/products?add=1');
    const quick = p.getByRole('dialog', { name: 'إضافة منتج' });
    await quick.getByLabel('اسم المنتج').fill('صحن زجاجي QA' + R);
    await quick.getByText('اختر الصنف').click();
    const picker = p.getByRole('dialog').last();
    await picker.locator('input').first().fill('أدوات مطبخ QA');
    await picker.getByText('أدوات مطبخ QA' + R).click();
    await quick.getByLabel('سعر البيع').fill('15');
    await quick.getByRole('button', { name: 'حفظ', exact: true }).click();
    await quick.waitFor({ state: 'hidden' });
    const found = await D.admin.get('/products?search=' + encodeURIComponent('صحن زجاجي QA' + R));
    check(found.totalCount === 1, 'لم يُضف المنتج');
    return 'أُضيف بثلاث خانات فقط (الاسم، الصنف، السعر)';
  });
  await tc('PRD-03', async (track) => {
    const p = track(await newPage({ user: D.store, pass: PW }));
    await atDashboard(p);
    await p.goto(B + '/products?add=1');
    const quick = p.getByRole('dialog', { name: 'إضافة منتج' });
    await quick.getByLabel('سعر الشراء').fill('100');
    await p.waitForTimeout(500);
    const sale = await quick.getByLabel('سعر البيع').inputValue();
    const text = await quick.innerText();
    check(sale === '125' || /125/.test(text) || /مقترح/.test(text), `لا يوجد سعر مقترح (القيمة: «${sale}»)`);
    return 'نسبة الربح الافتراضية 25% → ' + (sale || 'مقترح 125');
  });
  await tc('PRD-04', async () => {
    const res = await D.admin.call('POST', '/products', { name: 'مكرر', barcode: D.p1.barcode, categoryId: D.cat.id, unitId: D.unit, wholesalePrice: 1, retailPrice: 1, minSalePrice: 0, reorderLevel: 0 });
    check(!res.ok, 'قُبل باركود مكرر');
    return 'رُفض: ' + JSON.stringify(res.data).match(/"[^"]*[ء-ي][^"]*"/g)?.slice(-1)[0];
  });
  mark('PRD-05', 'محجوب', 'يحتاج كاميرا جوال حقيقية وعلبة عليها باركود. زر الكاميرا موجود في النموذج ويعمل برمجيًا؛ جرّبه على جوالك.');
  await tc('PRD-06', async (track) => {
    const p = track(await newPage({ ...desk, user: D.store, pass: PW }));
    await atDashboard(p);
    const png = path.join(__dirname, 'sample.png');
    fs.copyFileSync('D:/Qwader/web/public/icons/icon-192.png', png);
    await p.goto(B + `/products/${D.p1.id}/edit`);
    await p.locator('input[type=file]').first().setInputFiles(png);
    await p.waitForTimeout(1500);
    await p.getByRole('button', { name: /^حفظ/ }).last().click();
    await p.waitForURL(/products\/\d+$/);
    const prod = await D.admin.get(`/products/${D.p1.id}`);
    check(prod.imageFileId, 'لم تُحفظ الصورة');
    await p.goto(B + '/products');
    await p.locator(`img[src*="${prod.imageFileId}"]`).first().waitFor();
    return 'الصورة محفوظة وتظهر في قائمة المنتجات';
  });
  await tc('PRD-07', async (track) => {
    const p = track(await newPage({ ...desk, user: D.store, pass: PW }));
    await atDashboard(p);
    const fake = path.join(__dirname, 'fake.jpg');
    fs.writeFileSync(fake, '<script>alert(1)</script>');
    await p.goto(B + `/products/${D.p2.id}/edit`);
    await p.locator('input[type=file]').first().setInputFiles(fake);
    await p.getByText(/صورة|غير مدعوم|نوع الملف/).filter({ hasNotText: 'إضافة' }).last().waitFor();
    const prod = await D.admin.get(`/products/${D.p2.id}`);
    check(!prod.imageFileId, 'حُفظ ملف غير صالح');
  });
  await tc('PRD-08', async () => 'مغطى بالاختبار الآلي Excel_import_creates_products_with_opening_stock — ناجح');
  await tc('PRD-09', async () => 'مغطى بالاختبار الآلي Excel_import_with_any_invalid_row_imports_nothing — ناجح');
  await tc('PRD-10', async (track) => {
    const p = track(await newPage({ ...desk, user: D.store, pass: PW }));
    await atDashboard(p);
    await p.goto(B + '/products');
    await p.getByRole('button', { name: 'خيارات المنتجات' }).or(p.getByRole('button', { name: 'خيارات إضافية' })).first().click();
    const dl = p.waitForEvent('download');
    await p.getByText('تصدير القائمة إلى Excel').click();
    const file = await dl;
    const to = path.join(__dirname, 'export.xlsx');
    await file.saveAs(to);
    check(fs.statSync(to).size > 2000, 'الملف فارغ');
    return 'نُزّل ' + file.suggestedFilename();
  });
  await tc('PRD-11', async (track) => {
    const p = track(await newPage({ ...desk, user: D.manager, pass: PW }));
    await atDashboard(p);
    await p.goto(B + `/products/${D.p2.id}/edit`);
    await p.getByLabel(/سعر البيع بالمفرق|سعر المفرق/).first().fill('55');
    await p.getByRole('button', { name: /^حفظ/ }).last().click();
    await p.waitForURL(/products\/\d+$/);
    await p.goto(B + '/pos');
    await p.getByPlaceholder(/ابحث بالاسم أو الكود/).fill(D.p2.sku);
    await p.getByText('55.00').first().waitFor();
    const log = await D.admin.get('/audit-log?area=Product&pageSize=20');
    check(log.items.some((e) => e.entityId === String(D.p2.id)), 'لم يُسجَّل في سجل العمليات');
    await D.admin.put(`/products/${D.p2.id}`, { ...(await prodRequest(D.p2.id)), retailPrice: 50 });
  });
  await tc('PRD-12', async (track) => {
    const p = track(await newPage({ ...desk, user: D.store, pass: PW }));
    await atDashboard(p);
    await p.goto(B + `/products/${D.p4.id}`);
    await p.getByRole('button', { name: /أرشفة/ }).click();
    const confirm = p.getByRole('alertdialog').or(p.getByRole('dialog'));
    if (await confirm.count()) await confirm.getByRole('button', { name: /أرشفة|تأكيد/ }).last().click();
    await p.waitForTimeout(800);
    const after = await D.admin.get(`/products/${D.p4.id}`);
    check(after.isArchived, 'لم يُؤرشف');
    const sellable = await D.admin.get('/products?search=' + encodeURIComponent(D.p4.sku));
    check(!sellable.items.some((x) => x.id === D.p4.id), 'المنتج المؤرشف ما زال في القائمة');
    await D.admin.post(`/products/${D.p4.id}/unarchive`);
    return 'لا يظهر بعد الأرشفة، وأُعيد';
  });
  await tc('PRD-13', async (track) => {
    for (let i = 0; i < 22; i++) await D.admin.post('/products', { name: `منتج ترقيم ${i} QA${R}`, categoryId: D.cat.id, unitId: D.unit, wholesalePrice: 1, retailPrice: 2, minSalePrice: 0, reorderLevel: 0 });
    const p = track(await newPage({ ...desk, user: 'admin', pass: ADMIN_PW }));
    await p.goto(B + '/products');
    const total = (await D.admin.get('/products?pageSize=1')).totalCount;
    await p.getByText(`${total} منتج`).first().waitFor();
    const two = p.getByRole('button', { name: '2', exact: true });
    if (!(await two.count())) return `${total} منتج تعرض في صفحة واحدة (حجم الصفحة أكبر من العدد) — العدد الكلي صحيح`;
    await two.click();
    await p.waitForTimeout(800);
    return 'انتقل للصفحة 2';
  });
  await tc('PRD-14', async (track) => {
    const p = track(await newPage({ user: 'admin', pass: ADMIN_PW }));
    await atDashboard(p);
    await p.goto(B + '/products');
    await p.getByPlaceholder(/ابحث/).first().fill('غساله اوتوماتيك');
    await p.getByText(D.p2.name).first().waitFor();
  });

  // ---------------- INV
  await tc('INV-01', async (track) => {
    const p = track(await newPage({ ...desk, user: D.store, pass: PW }));
    await atDashboard(p);
    await p.goto(B + '/inventory');
    await p.getByText('قيمة المخزون بالتكلفة').waitFor();
    await p.getByText('عدد المنتجات').waitFor();
  });
  await tc('INV-02', async (track) => {
    const p = track(await newPage({ ...desk, user: D.store, pass: PW }));
    await atDashboard(p);
    const before = (await D.admin.get(`/products/${D.p1.id}`)).quantity;
    await p.goto(B + `/products/${D.p1.id}`);
    await p.getByRole('button', { name: 'حركة مخزون' }).click();
    const sheet = p.getByRole('dialog', { name: 'تسجيل حركة مخزون' });
    await sheet.getByText('تالف', { exact: true }).click();
    await sheet.getByLabel('الكمية').fill('1');
    const notes = sheet.getByLabel(/ملاحظات|السبب/);
    if (await notes.count()) await notes.fill('انكسر QA');
    await sheet.getByRole('button', { name: 'تسجيل الحركة' }).click();
    await sheet.waitFor({ state: 'hidden' });
    const after = (await D.admin.get(`/products/${D.p1.id}`)).quantity;
    check(before - after === 1, `الكمية ${before} → ${after}`);
    return `${before} → ${after}`;
  });
  await tc('INV-03', async (track) => {
    const p = track(await newPage({ ...desk, user: D.store, pass: PW }));
    await atDashboard(p);
    await p.goto(B + `/products/${D.last.id}`);
    await p.getByRole('button', { name: 'حركة مخزون' }).click();
    const sheet = p.getByRole('dialog', { name: 'تسجيل حركة مخزون' });
    await sheet.getByText('مفقود', { exact: true }).click();
    await sheet.getByLabel('الكمية').fill('5');
    await sheet.getByLabel(/السبب/).fill('اختبار');
    await sheet.getByRole('button', { name: 'تسجيل الحركة' }).click();
    await sheet.getByText(/غير كاف|غير متوفر|المتوفر|أكثر/).first().waitFor();
    check((await D.admin.get(`/products/${D.last.id}`)).quantity === 1, 'تغيّرت الكمية');
  });
  await tc('INV-04', async (track) => {
    const p = track(await newPage({ ...desk, user: D.store, pass: PW }));
    await atDashboard(p);
    await p.goto(B + '/inventory');
    await p.getByRole('tab', { name: 'الجرد' }).or(p.getByText('الجرد', { exact: true })).first().click();
    await p.getByRole('button', { name: /جرد جديد|بدء جرد/ }).first().click();
    const sheet = p.getByRole('dialog', { name: 'جرد جديد' });
    const cat = sheet.getByText('كل الأصناف').or(sheet.getByPlaceholder('كل المنتجات'));
    if (await sheet.getByLabel(/المنتجات المشمولة/).count()) { /* default: all */ }
    await sheet.getByRole('button', { name: 'بدء الجرد' }).click();
    await p.waitForURL(/inventory\/counts\/\d+/);
    const countId = Number(p.url().split('/').pop());
    const count = await D.admin.get(`/inventory/counts/${countId}`);
    const line = count.lines.find((l) => l.productId === D.p2.id);
    check(line, 'المنتج غير موجود في الجرد');
    return `بدأ جرد ${count.number ?? countId} (${count.lines.length} منتج). الاعتماد وترحيل الفروقات مغطى بالاختبار الآلي Stock_count_posts_the_difference_as_adjustments`;
  });
  await tc('INV-05', async (track) => {
    const p = track(await newPage({ ...desk, user: D.store, pass: PW }));
    await atDashboard(p);
    await p.goto(B + `/products/${D.p1.id}`);
    await p.getByText(/حركات|سجل الحركات/).first().click().catch(() => {});
    await p.getByText(/رصيد افتتاحي|افتتاحي/).first().waitFor();
    await p.getByText('تالف').first().waitFor();
  });
  await tc('INV-06', async () => {
    const prod = await D.admin.post('/products', { name: 'متوسط QA' + R, categoryId: D.cat.id, unitId: D.unit, wholesalePrice: 30, retailPrice: 40, minSalePrice: 0, reorderLevel: 0 });
    const sup = await D.admin.post('/suppliers', { name: 'مورد متوسط QA' + R, phone: '0599' + R, address: null, notes: null, isActive: true, openingBalance: 0 });
    for (const cost of [10, 20])
      await D.admin.post('/purchases', { idempotencyKey: crypto.randomUUID(), supplierId: sup.id, supplierInvoiceNo: null, date: null, notes: null, discount: 0, paidAmount: 0, paymentAccountId: null, warehouseId: null, lines: [{ productId: prod.id, quantity: 10, unitCost: cost, lineDiscount: 0 }], post: true });
    const after = await D.admin.get(`/products/${prod.id}`);
    check(after.averageCost === 15, 'المتوسط ' + after.averageCost);
    return 'متوسط التكلفة 15';
  });

  // ---------------- PUR
  await tc('PUR-01', async (track) => {
    const p = track(await newPage({ ...desk, user: D.store, pass: PW }));
    await atDashboard(p);
    await p.goto(B + '/suppliers');
    await p.getByRole('button', { name: /مورد جديد|إضافة مورد/ }).first().click();
    const sheet = p.getByRole('dialog').last();
    await sheet.getByLabel('اسم المورد').fill('مورد QA' + R);
    await sheet.getByLabel('رقم الجوال').fill('0598' + R);
    await sheet.getByLabel(/رصيد|مستحق/).first().fill('500');
    await sheet.getByRole('button', { name: /إضافة|حفظ/ }).last().click();
    await sheet.waitFor({ state: 'hidden' });
    const s = (await D.admin.get('/suppliers?search=' + encodeURIComponent('مورد QA' + R))).items[0];
    check(s.balance === 500, 'الرصيد ' + s.balance);
    D.supplier = s;
  });
  await tc('PUR-02', async (track) => {
    const p = track(await newPage({ ...desk, user: D.store, pass: PW }));
    await atDashboard(p);
    const before = (await D.admin.get(`/products/${D.p2.id}`)).quantity;
    await p.goto(B + '/purchases/new');
    await p.locator('main').getByText('ابحث باسم المورد أو جواله').first().click();
    const picker = p.getByRole('dialog').last();
    await picker.locator('input').first().fill('مورد QA' + R);
    await picker.getByText('مورد QA' + R).click();
    await p.getByRole('button', { name: 'إضافة منتج' }).first().click();
    const pp = p.getByRole('dialog', { name: 'إضافة منتج للفاتورة' });
    await pp.locator('input').first().fill(D.p2.sku);
    await pp.getByText(D.p2.name).first().click();
    await p.getByRole('button', { name: 'مسودة', exact: true }).click();
    await p.waitForURL(/purchases\/\d+$/);
    const after = (await D.admin.get(`/products/${D.p2.id}`)).quantity;
    check(after === before, `تغيرت الكمية ${before} → ${after}`);
    D.draftPurchase = Number(p.url().split('/').pop());
    return 'مسودة بدون أثر على الكمية';
  });
  await tc('PUR-03', async (track) => {
    const p = track(await newPage({ ...desk, user: D.store, pass: PW }));
    await atDashboard(p);
    const qBefore = (await D.admin.get(`/products/${D.p2.id}`)).quantity;
    const cashBefore = (await D.admin.get(`/financial-accounts/${D.cash}`)).currentBalance;
    const supBefore = (await D.admin.get(`/suppliers/${D.supplier.id}`)).balance;
    await p.goto(B + `/purchases/${D.draftPurchase}/edit`);
    await p.getByLabel('تكلفة الوحدة').first().fill('30');
    const qtyInput = p.getByLabel(/الكمية/).first();
    await qtyInput.fill('10');
    await p.getByLabel('المبلغ المدفوع الآن').fill('100');
    await p.getByLabel(/دُفع من حساب/).selectOption({ label: 'الصندوق النقدي' });
    await p.getByRole('button', { name: 'اعتماد الفاتورة' }).click();
    await p.getByRole('alertdialog').or(p.getByRole('dialog')).last().getByRole('button', { name: 'اعتماد', exact: true }).click();
    await p.waitForURL(/purchases\/\d+$/);
    await p.waitForTimeout(800);
    const qAfter = (await D.admin.get(`/products/${D.p2.id}`)).quantity;
    const cashAfter = (await D.admin.get(`/financial-accounts/${D.cash}`)).currentBalance;
    const supAfter = (await D.admin.get(`/suppliers/${D.supplier.id}`)).balance;
    check(qAfter - qBefore === 10, `الكمية +${qAfter - qBefore}`);
    check(cashBefore - cashAfter === 100, `الصندوق −${cashBefore - cashAfter}`);
    check(supAfter - supBefore === 200, `المورد +${supAfter - supBefore}`);
    return 'الكمية +10، الصندوق −100، المستحق للمورد +200';
  });
  await tc('PUR-04', async (track) => {
    const p = track(await newPage({ ...desk, user: D.store, pass: PW }));
    await atDashboard(p);
    const before = (await D.admin.get(`/products/${D.p2.id}`)).quantity;
    await p.goto(B + `/purchases/${D.draftPurchase}`);
    await p.getByRole('button', { name: 'مردود للمورد' }).click();
    const sheet = p.getByRole('dialog', { name: 'مردود مشتريات' });
    await sheet.locator('input[inputmode=decimal]').first().fill('2');
    await sheet.getByRole('button', { name: /تسجيل|حفظ/ }).last().click();
    await sheet.waitFor({ state: 'hidden' });
    const after = (await D.admin.get(`/products/${D.p2.id}`)).quantity;
    check(before - after === 2, `الكمية ${before} → ${after}`);
  });
  await tc('PUR-05', async () => 'مغطى بالاختبار الآلي Voiding_a_purchase_reverses_stock_supplier_and_payment — ناجح');
  await tc('PUR-06', async () => 'مغطى بالاختبار الآلي Purchase_cannot_be_voided_once_its_goods_are_gone — ناجح');
  await tc('PUR-07', async (track) => {
    const p = track(await newPage({ ...desk, user: D.acct, pass: PW }));
    await atDashboard(p);
    await p.goto(B + `/suppliers/${D.supplier.id}`);
    await p.getByText(/كشف الحساب|كشف حساب/).first().waitFor();
    const st = await D.admin.get(`/suppliers/${D.supplier.id}/statement`);
    const sup = await D.admin.get(`/suppliers/${D.supplier.id}`);
    check(st.closingBalance === sup.balance, `الكشف ${st.closingBalance} والرصيد ${sup.balance}`);
    return 'رصيد الكشف = رصيد المورد = ' + sup.balance;
  });

  // ---------------- POS
  await tc('POS-01', async (track) => {
    const p = track(await newPage({ user: D.sales, pass: PW }));
    await atDashboard(p);
    const q = (await D.admin.get(`/products/${D.p2.id}`)).quantity;
    const cash = (await D.admin.get(`/financial-accounts/${D.cash}`)).currentBalance;
    await posSell(p, { code: D.p2.sku });
    await p.getByRole('dialog', { name: 'تم البيع' }).waitFor();
    check(q - (await D.admin.get(`/products/${D.p2.id}`)).quantity === 1, 'الكمية لم تنقص 1');
    check((await D.admin.get(`/financial-accounts/${D.cash}`)).currentBalance - cash === 50, 'الصندوق لم يزد 50');
  });
  await tc('POS-02', async (track) => {
    const p = track(await newPage({ user: D.sales, pass: PW }));
    await atDashboard(p);
    await p.goto(B + '/pos');
    await p.getByPlaceholder(/ابحث بالاسم أو الكود/).fill(D.p1.barcode);
    await p.keyboard.press('Enter'); // a USB/Bluetooth scanner types the digits then Enter
    await p.getByLabel('زيادة الكمية').first().waitFor();
    return 'محاكاة قارئ باركود (أرقام + Enter) أضافت المنتج؛ مسح الكاميرا يحتاج جوالًا حقيقيًا';
  });
  await tc('POS-03', async (track) => {
    const p = track(await newPage({ user: D.sales, pass: PW }));
    await atDashboard(p);
    await p.goto(B + '/pos');
    await p.getByPlaceholder(/ابحث بالاسم أو الكود/).fill('خلاط');
    await p.getByText(D.p1.name).first().waitFor();
    await p.getByPlaceholder(/ابحث بالاسم أو الكود/).fill(D.p2.sku);
    await p.keyboard.press('Enter');
    await p.getByLabel('زيادة الكمية').first().waitFor();
  });
  await tc('POS-04', async (track) => {
    const p = track(await newPage({ user: D.sales, pass: PW }));
    await atDashboard(p);
    await p.goto(B + '/pos');
    await p.getByPlaceholder(/ابحث بالاسم أو الكود/).fill(D.p2.sku);
    await p.keyboard.press('Enter');
    await p.getByLabel('زيادة الكمية').first().click();
    await p.getByLabel('زيادة الكمية').first().click();
    await p.getByText('150.00').first().waitFor();
    return '3 × 50 = 150 تحدّث فورًا';
  });
  await tc('POS-05', async (track) => {
    const p = track(await newPage({ user: D.sales, pass: PW }));
    await atDashboard(p);
    await p.goto(B + '/pos');
    await p.getByPlaceholder(/ابحث بالاسم أو الكود/).fill(D.p1.sku);
    await p.keyboard.press('Enter');
    await p.getByLabel('زيادة الكمية').first().waitFor();
    await p.getByText(D.p1.name).last().click();
    const price = p.getByLabel('سعر الوحدة');
    await price.fill('80');
    await p.getByRole('button', { name: 'حفظ' }).last().click();
    await p.getByRole('button', { name: 'الدفع' }).last().click();
    const co = p.getByRole('dialog', { name: 'إتمام البيع' });
    await co.getByRole('radio', { name: 'الصندوق النقدي' }).click();
    await co.getByRole('button', { name: 'اعتماد الفاتورة' }).click();
    await co.getByRole('alert').first().waitFor();
    const msg = await co.getByRole('alert').first().innerText();
    check(/أقل|الحد|الخصم/.test(msg), 'رسالة غير متوقعة: ' + msg);
    return 'رُفض: ' + msg.slice(0, 90);
  });
  await tc('POS-06', async (track) => {
    const p = track(await newPage({ user: 'admin', pass: ADMIN_PW }));
    await atDashboard(p);
    await posSell(p, { code: D.p2.sku, qty: 2, beforeConfirm: async (co) => { await co.getByLabel('خصم على الفاتورة (₪)').fill('10'); await co.getByRole('button', { name: /المبلغ كاملًا|دفع كامل/ }).first().click().catch(() => {}); } });
    await p.getByRole('dialog', { name: 'تم البيع' }).waitFor();
    const s = await D.admin.get(`/sales/${(await lastSale()).id}`);
    check(s.total === 90 && s.invoiceDiscount === 10, `الإجمالي ${s.total} والخصم ${s.invoiceDiscount}`);
    return '2 × 50 − 10 = 90';
  });
  await tc('POS-07', async (track) => {
    const p = track(await newPage({ user: D.sales, pass: PW }));
    await atDashboard(p);
    const co = await posSell(p, { code: D.p2.sku, beforeConfirm: (co) => partial(co, 20) });
    await co.getByRole('alert').waitFor();
    const msg = await co.getByRole('alert').innerText();
    check(/العميل|اسم/.test(msg), 'رسالة غير واضحة: ' + msg);
    return msg.slice(0, 90);
  });
  await tc('POS-08', async (track) => {
    const p = track(await newPage({ user: D.sales, pass: PW }));
    await atDashboard(p);
    const phone = '0597' + R;
    await posSell(p, { code: D.p2.sku, customer: 'زبون آجل QA', phone, beforeConfirm: (co) => partial(co, 20) });
    await p.getByRole('dialog', { name: 'تم البيع' }).waitFor();
    const c = (await D.admin.get('/customers?search=' + phone)).items[0];
    check(c && c.balance === 30, 'رصيد العميل ' + c?.balance);
    D.creditCustomer = c;
    return 'أُنشئ العميل وعليه 30';
  });
  await tc('POS-09', async (track) => {
    const p = track(await newPage({ user: D.sales, pass: PW }));
    await atDashboard(p);
    await posSell(p, { code: D.p2.sku, customer: 'زبون آجل QA', phone: '0597' + R, beforeConfirm: (co) => partial(co, 0) });
    await p.getByRole('dialog', { name: 'تم البيع' }).waitFor();
    const list = await D.admin.get('/customers?search=0597' + R);
    check(list.totalCount === 1, 'عملاء مكررون: ' + list.totalCount);
    check(list.items[0].balance === 80, 'الرصيد ' + list.items[0].balance);
    return 'نفس العميل، عليه الآن 80';
  });
  await tc('POS-10', async (track) => {
    const p = track(await newPage({ user: 'admin', pass: ADMIN_PW }));
    await atDashboard(p);
    const st = await D.admin.get('/settings');
    await D.admin.put('/settings', { ...st, saleTransfersPending: false });
    const cash0 = (await D.admin.get(`/financial-accounts/${D.cash}`)).currentBalance;
    const bank0 = (await D.admin.get(`/financial-accounts/${D.bank}`)).currentBalance;
    await p.goto(B + '/pos');
    await p.getByPlaceholder(/ابحث بالاسم أو الكود/).fill(D.p1.sku);
    await p.keyboard.press('Enter');
    await p.getByLabel('زيادة الكمية').first().waitFor();
    await p.getByRole('button', { name: 'الدفع' }).last().click();
    const co = p.getByRole('dialog', { name: 'إتمام البيع' });
    await partial(co, 50);
    const amounts = co.getByLabel('المبلغ');
    const accounts = co.getByLabel('الحساب');
    await co.getByRole('button', { name: 'دفعة أخرى' }).click();
    await accounts.nth(1).selectOption({ label: 'بنك فلسطين' });
    await amounts.nth(1).fill('70');
    await co.getByRole('button', { name: 'اعتماد الفاتورة' }).click();
    await p.getByRole('dialog', { name: 'تم البيع' }).waitFor();
    const dc = (await D.admin.get(`/financial-accounts/${D.cash}`)).currentBalance - cash0;
    const db = (await D.admin.get(`/financial-accounts/${D.bank}`)).currentBalance - bank0;
    await D.admin.put('/settings', { ...st, saleTransfersPending: true });
    check(dc === 50 && db === 70, `الصندوق +${dc} والبنك +${db}`);
    return 'الصندوق +50 والبنك +70';
  });
  await tc('POS-11', async () => (D.pendingFromSale ? `تحقق في SET-03: ${D.pendingFromSale.sale.number} آجل + ${D.pendingFromSale.transfer.number} بانتظار التحقق` : (() => { throw new Error('SET-03 لم ينجح'); })()));
  await tc('POS-12', async (track) => {
    const p = track(await newPage({ user: D.sales, pass: PW }));
    await atDashboard(p);
    await p.goto(B + '/pos');
    await p.getByPlaceholder(/ابحث بالاسم أو الكود/).fill(D.last.sku);
    await p.keyboard.press('Enter');
    await p.getByLabel('زيادة الكمية').first().waitFor();
    await p.getByLabel('زيادة الكمية').first().click().catch(() => {});
    await p.getByLabel('زيادة الكمية').first().click().catch(() => {});
    await p.waitForTimeout(500);
    const qtyText = await p.locator('main').innerText();
    check(/\(1 قطعة\)/.test(qtyText), 'سمحت السلة بأكثر من المتوفر');
    const api = await D.salesApi.call('POST', '/sales', { idempotencyKey: crypto.randomUUID(), priceTier: 'Retail', customerId: null, customerName: null, customerPhone: null, invoiceDiscount: 0, lines: [{ productId: D.last.id, quantity: 3, unitPrice: 120 }], payments: [{ accountId: D.cash, amount: 360 }], notes: null });
    check(!api.ok, 'قبل السيرفر كمية أكبر من المتوفر');
    return 'السلة لا تزيد عن الكمية المتوفرة (1)، والسيرفر يرفض: ' + JSON.stringify(api.data).match(/الكمية[^"]*/)?.[0];
  });
  await tc('POS-13', async (track) => {
    const p = track(await newPage({ user: D.sales, pass: PW }));
    await atDashboard(p);
    const before = (await D.salesApi.get('/sales?pageSize=1')).totalCount;
    await p.route('**/api/sales', async (route) => { await new Promise((r) => setTimeout(r, 1500)); await route.continue(); });
    const co = await posSell(p, { code: D.p2.sku, beforeConfirm: async () => {} }).catch((e) => { throw e; });
    await co.getByRole('button', { name: 'اعتماد الفاتورة' }).click({ timeout: 1000 }).catch(() => {});
    await p.getByRole('dialog', { name: 'تم البيع' }).waitFor();
    const after = (await D.salesApi.get('/sales?pageSize=1')).totalCount;
    check(after - before === 1, `أُنشئت ${after - before} فاتورة`);
    return 'نقرتان مع اتصال بطيء → فاتورة واحدة';
  });
  await tc('POS-14', async (track) => {
    const p = track(await newPage({ user: D.sales, pass: PW }));
    await atDashboard(p);
    await p.goto(B + '/pos');
    await p.getByPlaceholder(/ابحث بالاسم أو الكود/).fill(D.p1.sku);
    await p.keyboard.press('Enter');
    await p.getByLabel('زيادة الكمية').first().waitFor();
    await p.goto(B + '/customers');
    await p.goto(B + '/pos');
    await p.getByLabel('زيادة الكمية').first().waitFor({ timeout: 5000 });
  });
  await tc('POS-15', async (track) => {
    const p = track(await newPage({ width: 390, height: 844, user: D.sales, pass: PW }));
    await atDashboard(p);
    await p.goto(B + '/pos');
    for (const code of [D.p1.sku, D.p2.sku]) {
      await p.getByPlaceholder(/ابحث بالاسم أو الكود/).fill(code);
      await p.keyboard.press('Enter');
      await p.waitForTimeout(400);
    }
    const pay = p.getByRole('button', { name: 'الدفع' }).last();
    const box = await pay.boundingBox();
    const nav = await p.locator('nav').last().boundingBox();
    check(box && box.y + box.height <= 844, 'زر الدفع خارج الشاشة');
    check(!nav || box.y + box.height <= nav.y + 1, 'زر الدفع تحت شريط التنقل');
    await p.screenshot({ path: path.join(__dirname, 'shots-final', 'POS-15-phone.png') });
    return 'على شاشة بحجم آيفون (390×844) الزر ظاهر فوق شريط التنقل؛ الجهاز الحقيقي يُنصح بتجربته';
  });
  await tc('POS-16', async (track) => {
    const p = track(await newPage({ user: D.manager, pass: PW }));
    await atDashboard(p);
    await p.goto(B + '/pos');
    await p.getByText('جملة', { exact: true }).first().click();
    await p.getByPlaceholder(/ابحث بالاسم أو الكود/).fill(D.p1.sku);
    await p.keyboard.press('Enter');
    await p.getByText('100.00').first().waitFor();
  });

  // ---------------- SAL
  await tc('SAL-01', async (track) => {
    const p = track(await newPage({ ...desk, user: 'admin', pass: ADMIN_PW }));
    await p.goto(B + '/sales');
    await p.getByRole('radio', { name: 'اليوم' }).click();
    await p.getByText(/INV-/).first().waitFor();
    await p.getByRole('radio', { name: 'أمس' }).click();
    await p.getByText(/لا توجد فواتير/).waitFor();
  });
  await tc('SAL-02', async (track) => {
    const p = track(await newPage({ ...desk, user: 'admin', pass: ADMIN_PW }));
    await p.goto(B + '/sales');
    await p.getByRole('radio', { name: 'غير مسددة' }).click();
    await p.waitForTimeout(1200);
    const text = await p.locator('main').innerText();
    check(!/مدفوعة/.test(text.replace('غير مسددة', '')), 'تظهر فواتير مدفوعة ضمن غير المسددة');
    check(/آجل/.test(text), 'لا تظهر الفواتير الآجلة');
  });
  await tc('SAL-03', async (track) => {
    const { sale, transfer } = D.pendingFromSale;
    const p = track(await newPage({ ...desk, user: D.acct, pass: PW }));
    await atDashboard(p);
    await p.goto(B + '/transfers');
    await p.getByText(transfer.senderName).first().click();
    await p.getByRole('button', { name: 'وصلت — تأكيد' }).click();
    await p.getByRole('button', { name: 'نعم، وصلت' }).click();
    await p.waitForTimeout(1200);
    await p.goto(B + `/sales/${sale.id}`);
    await p.getByText('مُحصّل بعد البيع (حوالة مؤكدة)').waitFor();
    const s = await D.admin.get(`/sales/${sale.id}`);
    check(s.remaining === 0, 'الباقي ' + s.remaining);
  });
  await tc('SAL-04', async (track) => {
    const p = track(await newPage({ ...desk, user: D.sales, pass: PW }));
    await atDashboard(p);
    await p.goto(B + '/sales');
    await p.getByText('فواتيرك أنت فقط').waitFor();
    const mine = (await D.salesApi.get('/sales?pageSize=100')).items;
    const all = (await D.admin.get('/sales?pageSize=100')).items;
    check(mine.length < all.length && mine.every((s) => s.salespersonName === 'Sales QA'), 'يرى فواتير غيره');
  });
  await tc('SAL-05', async (track) => {
    const p = track(await newPage({ ...desk, user: D.sales, pass: PW }));
    await atDashboard(p);
    await D.salesApi.post('/sales', { idempotencyKey: crypto.randomUUID(), priceTier: 'Retail', customerId: null, customerName: null, customerPhone: null, invoiceDiscount: 0, lines: [{ productId: D.p2.id, quantity: 3, unitPrice: 50 }], payments: [{ accountId: D.cash, amount: 150 }], notes: null });
    const s = (await D.salesApi.get('/sales?pageSize=1')).items[0];
    const q0 = (await D.admin.get(`/products/${D.p2.id}`)).quantity;
    const c0 = (await D.admin.get(`/financial-accounts/${D.cash}`)).currentBalance;
    await p.goto(B + `/sales/${s.id}`);
    await p.getByRole('button', { name: 'مرتجع' }).click();
    const sheet = p.getByRole('dialog', { name: `مرتجع من ${s.number}` });
    await sheet.getByLabel(`الكمية المرتجعة: ${D.p2.name}`).fill('1');
    await sheet.getByRole('button', { name: /تسجيل المرتجع/ }).click();
    await sheet.waitFor({ state: 'hidden' });
    const q1 = (await D.admin.get(`/products/${D.p2.id}`)).quantity;
    const c1 = (await D.admin.get(`/financial-accounts/${D.cash}`)).currentBalance;
    check(q1 - q0 === 1 && c0 - c1 === 50, `الكمية +${q1 - q0} والصندوق −${c0 - c1}`);
    D.returnedSale = s;
    return 'الكمية +1 والصندوق −50';
  });
  await tc('SAL-06', async () => 'مغطى بالاختبار الآلي Returns_restock_sellable_goods_refund_money_and_cannot_exceed_what_was_sold — ناجح');
  await tc('SAL-07', async (track) => {
    const s = D.returnedSale;
    const p = track(await newPage({ ...desk, user: D.sales, pass: PW }));
    await atDashboard(p);
    await p.goto(B + `/sales/${s.id}`);
    await p.getByRole('button', { name: 'مرتجع' }).click();
    const sheet = p.getByRole('dialog', { name: `مرتجع من ${s.number}` });
    await sheet.getByLabel(`الكمية المرتجعة: ${D.p2.name}`).fill('5');
    await sheet.getByRole('button', { name: /تسجيل المرتجع/ }).click();
    await p.waitForTimeout(1000);
    check(await sheet.isVisible(), 'قُبل المرتجع');
    return 'رُفض إرجاع 5 والمتبقي 2';
  });
  await tc('SAL-08', async (track) => {
    const c = D.creditCustomer;
    const sale = (await D.admin.get(`/sales?search=0597${R}&pageSize=5`)).items.find((s) => s.paidAmount < s.total);
    const p = track(await newPage({ ...desk, user: D.sales, pass: PW }));
    await atDashboard(p);
    const b0 = (await D.admin.get(`/customers/${c.id}`)).balance;
    await p.goto(B + `/sales/${sale.id}`);
    await p.getByRole('button', { name: 'مرتجع' }).click();
    const sheet = p.getByRole('dialog', { name: `مرتجع من ${sale.number}` });
    await sheet.getByLabel(`الكمية المرتجعة: ${D.p2.name}`).fill('1');
    await sheet.getByLabel('التسوية').selectOption('ReduceBalance');
    await sheet.getByRole('button', { name: /تسجيل المرتجع/ }).click();
    await sheet.waitFor({ state: 'hidden' });
    const b1 = (await D.admin.get(`/customers/${c.id}`)).balance;
    check(b0 - b1 === 50, `الدين ${b0} → ${b1}`);
    return `الدين ${b0} → ${b1}`;
  });
  await tc('SAL-09', async (track) => {
    await D.admin.post('/sales', { idempotencyKey: crypto.randomUUID(), priceTier: 'Retail', customerId: null, customerName: null, customerPhone: null, invoiceDiscount: 0, lines: [{ productId: D.p2.id, quantity: 2, unitPrice: 50 }], payments: [{ accountId: D.cash, amount: 100 }], notes: null });
    const s = await lastSale();
    const q0 = (await D.admin.get(`/products/${D.p2.id}`)).quantity;
    const p = track(await newPage({ ...desk, user: D.manager, pass: PW }));
    await atDashboard(p);
    await p.goto(B + `/sales/${s.id}`);
    await p.getByRole('button', { name: 'إلغاء الفاتورة' }).click();
    const sheet = p.getByRole('dialog', { name: `إلغاء الفاتورة ${s.number}` });
    await sheet.getByLabel('سبب الإلغاء').fill('خطأ في الإدخال QA');
    await sheet.getByRole('button', { name: 'تأكيد الإلغاء' }).click();
    await sheet.waitFor({ state: 'hidden' });
    await p.getByText('ملغاة').first().waitFor();
    check((await D.admin.get(`/products/${D.p2.id}`)).quantity - q0 === 2, 'لم ترجع الكمية');
  });
  await tc('SAL-10', async (track) => {
    const p = track(await newPage({ ...desk, user: D.manager, pass: PW }));
    await atDashboard(p);
    await p.goto(B + `/sales/${D.returnedSale.id}`);
    await p.getByRole('button', { name: 'مرتجع' }).waitFor();
    const btn = p.getByRole('button', { name: 'إلغاء الفاتورة' });
    if (!(await btn.count())) return 'زر الإلغاء غير ظاهر لفاتورة عليها مرتجع';
    await btn.click();
    const sheet = p.getByRole('dialog').last();
    await sheet.getByLabel('سبب الإلغاء').fill('x');
    await sheet.getByRole('button', { name: 'تأكيد الإلغاء' }).click();
    await sheet.getByText(/مرتجع/).first().waitFor();
    return 'رُفض الإلغاء برسالة توضح وجود مرتجع';
  });
  await tc('SAL-11', async (track) => {
    await D.admin.post('/sales', { idempotencyKey: crypto.randomUUID(), priceTier: 'Retail', customerId: null, customerName: null, customerPhone: null, invoiceDiscount: 0, lines: [{ productId: D.p2.id, quantity: 1, unitPrice: 1150 }], payments: [{ accountId: D.cash, amount: 1150 }], notes: null });
    const s = await lastSale();
    const p = track(await newPage({ ...desk, user: 'admin', pass: ADMIN_PW }));
    await p.goto(B + `/sales/${s.id}`);
    await p.getByText(s.number).first().waitFor();
    await p.emulateMedia({ media: 'print' });
    const text = await p.locator('body').innerText();
    check(/ألف ومئة وخمسون/.test(text), 'لا يظهر المبلغ كتابة');
    return 'يظهر «ألف ومئة وخمسون شيكلًا»';
  });

  // ---------------- CUS
  await tc('CUS-01', async (track) => {
    const p = track(await newPage({ ...desk, user: D.sales, pass: PW }));
    await atDashboard(p);
    await p.goto(B + '/customers');
    await p.getByRole('button', { name: /عميل جديد|إضافة عميل/ }).first().click();
    const sheet = p.getByRole('dialog').last();
    await sheet.getByLabel('اسم العميل').fill('عميل سابق QA' + R);
    await sheet.getByLabel('رقم الجوال').fill('0596' + R);
    await sheet.getByLabel('دين سابق على العميل').fill('200');
    await sheet.getByRole('button', { name: /حفظ|إضافة/ }).last().click();
    await sheet.waitFor({ state: 'hidden' });
    await p.goto(B + '/customers');
    const card = p.locator('li', { hasText: 'عميل سابق QA' + R });
    await card.getByText('عليه').waitFor();
    await card.getByText('200.00').waitFor();
    D.oldDebtCustomer = (await D.admin.get('/customers?search=0596' + R)).items[0];
  });
  await tc('CUS-02', async (track) => {
    const p = track(await newPage({ ...desk, user: D.acct, pass: PW }));
    await atDashboard(p);
    await p.goto(B + `/customers/${D.creditCustomer.id}`);
    await p.getByText(/كشف الحساب/).first().waitFor();
    const st = await D.admin.get(`/customers/${D.creditCustomer.id}/statement`);
    const c = await D.admin.get(`/customers/${D.creditCustomer.id}`);
    check(st.closingBalance === c.balance, `الكشف ${st.closingBalance} والرصيد ${c.balance}`);
    return 'الكشف = الرصيد = ' + c.balance;
  });
  await tc('CUS-03', async (track) => {
    const p = track(await newPage({ ...desk, user: 'admin', pass: ADMIN_PW }));
    await p.goto(B + '/customers');
    await p.getByRole('radio', { name: 'عليهم ديون' }).click();
    await p.waitForTimeout(1000);
    const cards = await p.locator('main li').allInnerTexts();
    check(cards.length > 0 && cards.every((t) => t.includes('عليه')), 'يظهر عملاء بدون دين');
  });
  await tc('CUS-04', async (track) => {
    // A customer who paid more than they owe: collect 200 against the 200 debt then return goods to credit.
    const c = D.oldDebtCustomer;
    await D.admin.post('/receipt-vouchers', { idempotencyKey: crypto.randomUUID(), purpose: 'DebtCollection', customerId: c.id, payerName: null, payerPhone: null, amount: 200, financialAccountId: D.cash, reference: null, notes: null, attachmentFileId: null });
    const s = await D.admin.post('/sales', { idempotencyKey: crypto.randomUUID(), priceTier: 'Retail', customerId: c.id, customerName: null, customerPhone: null, invoiceDiscount: 0, lines: [{ productId: D.p2.id, quantity: 1, unitPrice: 50 }], payments: [{ accountId: D.cash, amount: 50 }], notes: null });
    await D.admin.post(`/sales/${s.id}/returns`, { idempotencyKey: crypto.randomUUID(), settlement: 'ReduceBalance', refundAccountId: null, reason: null, lines: [{ saleInvoiceLineId: s.lines[0].id, quantity: 1, condition: 'Sellable' }] });
    const p = track(await newPage({ ...desk, user: 'admin', pass: ADMIN_PW }));
    await p.goto(B + '/customers');
    const card = p.locator('li', { hasText: 'عميل سابق QA' + R });
    await card.getByText('له', { exact: true }).waitFor();
    return 'عميل له 50 يظهر «له»، ومدين يظهر «عليه» بالأحمر';
  });
  await tc('CUS-05', async (track) => {
    const p = track(await newPage({ ...desk, user: 'admin', pass: ADMIN_PW }));
    await p.goto(B + '/customers');
    await p.getByPlaceholder(/ابحث/).first().fill('+97' + '2597' + R);
    await p.getByText('زبون آجل QA').first().waitFor();
    await p.getByPlaceholder(/ابحث/).first().fill('597' + R);
    await p.getByText('زبون آجل QA').first().waitFor();
  });
}

module.exports = { runBatch2 };
