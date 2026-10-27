# نظام معرض قويدر للأدوات والأجهزة المنزلية

نظام عربي (RTL) لإدارة المبيعات والمخزون والمشتريات والحسابات، بالشيكل (₪).
التصميم الكامل في [docs/DESIGN.md](docs/DESIGN.md).

| الطبقة | التقنية |
|---|---|
| الخادم | ASP.NET Core 9 Web API ‏(`src/`) |
| قاعدة البيانات | SQL Server + EF Core Migrations |
| الواجهة | React 19 + TypeScript + Vite + Tailwind ‏(`web/`) |
| الاختبارات | xUnit على SQL Server حقيقي ‏(`tests/`) |

## المتطلبات
- .NET SDK 9
- Node.js 22
- SQL Server (النسخة الافتراضية المحلية `.` في بيئة التطوير)

## التشغيل لأول مرة (التطوير)

الأسرار لا تُحفظ في الكود؛ تُضبط مرة واحدة عبر user-secrets:

```bash
cd src/Qwader.Api
dotnet user-secrets set "Jwt:SigningKey" "<مفتاح عشوائي لا يقل عن 32 حرفًا>"
dotnet user-secrets set "Seed:AdminPassword" "<كلمة مرور المدير الأولى>"
```

ثم:

```bash
# 1) الخادم — ينشئ قاعدة البيانات Qwader ويطبّق الترحيلات والبيانات الأولية تلقائيًا
cd src/Qwader.Api
dotnet run --launch-profile http          # http://localhost:5298  (Swagger: /swagger)

# 2) الواجهة (نافذة أخرى)
cd web
npm install
npm run dev                               # http://localhost:5173
```

ادخل باسم المستخدم `admin` وكلمة المرور التي ضبطتها، وسيطلب النظام تغييرها عند أول دخول.

**التجربة من الهاتف:** الهاتف والكمبيوتر على نفس الشبكة ← افتح `http://<IP الكمبيوتر>:5173`.

## الاختبارات

```bash
dotnet test                       # الخادم: ينشئ قاعدة Qwader_IntegrationTests ويحذفها عند كل تشغيل
cd web && npx tsc -b --noEmit     # فحص الأنواع
cd web && npx vitest run          # اختبارات الواجهة والـ Worker
```

- **حالات الاختبار اليدوية:** [docs/Qwader-Test-Cases.xlsx](docs/Qwader-Test-Cases.xlsx) — 149 حالة في 20 قسمًا مع آخر نتيجة لكل حالة،
  ومصدرها [tools/test-cases](tools/test-cases) (`cases.py` ← `build.py`).
- **تنفيذها على الشاشات آليًا:** [tools/test-cases/ui-run](tools/test-cases/ui-run) (Playwright على نسخة اختبار محلية، **لا تشغّله على الموقع الحقيقي**).
- **بيانات تجريبية كبيرة:** [docs/استيراد-تجريبي](docs/استيراد-تجريبي) — 1000 صنف و4000 منتج بصيغة الاستيراد (ومولّدها في [tools/bulk-import](tools/bulk-import)).

## قواعد العمل المهمة

| الموضوع | القاعدة |
|---|---|
| تعديل السعر عند البيع | مسموح لأي بائع، **ولا يقل عن «أقل سعر بيع» لأحد** (حتى المدير). تعديل السعر ليس خصمًا؛ الخصم (على البند أو الفاتورة) يحتاج صلاحية ويخضع لحد الخصم في الإعدادات. البيع تحت التكلفة يحتاج صلاحية خاصة. |
| الأرقام في لوحة التحكم والتقارير | **المبيعات (الإيرادات)** = المبيعات − الخصومات − المرتجعات · **إجمالي الربح** = المبيعات − سعر الشراء · **المصروفات** = رواتب وأعطال ومصاريف تشغيلية · **صافي الربح** = إجمالي الربح − المصروفات. التالف والمفقود وزيادة الجرد تغيّر **رأس المال في البضاعة** (قيمة المخزون) ولا تدخل في الربح ولا المصروفات. |
| الحوالات عند البيع | كل دفعة غير نقدية تنشئ حوالة تلقائيًا؛ الإعداد يحدد إن كانت «بانتظار التحقق» (تبقى دينًا) أو «مؤكدة مباشرة». تأكيد حوالة أكبر من دين العميل يطلب تأكيدًا إضافيًا ويذكر الحوالة المكررة المحتملة. |
| تحصيل الديون | أي مبلغ يُحصَّل لاحقًا (سند قبض أو حوالة مؤكدة) يسدّد: فاتورة الحوالة أولًا، ثم الدين السابق، ثم الفواتير الأقدم (جدول `ReceiptAllocations`). إلغاء السند يعيد الفواتير غير مسددة. |
| المخزون | متوسط تكلفة مرجّح؛ لا يصبح المخزون سالبًا ولو باع جهازان آخر قطعة في نفس اللحظة. |

## النسخ الاحتياطي والتصفير (الإعدادات — لمدير النظام فقط)

- **تنزيل نسخة احتياطية:** ملف ZIP فيه كل الجداول (JSON) ونسخ Excel من المنتجات والعملاء والموردين والحسابات والصور المرفوعة،
  بدون كلمات المرور وجلسات الدخول ومفاتيح الإشعارات. يُنصح بتنزيلها أسبوعيًا وحفظها خارج الخادم.
- **تصفير النظام:** «مسح الحركات فقط» أو «مسح كل شيء»، بكتابة «تصفير» وكلمة مرور المدير. لا رجعة فيه — نزّل نسخة احتياطية قبله.

## رقم الإصدار

`App:Version` في [src/Qwader.Api/appsettings.json](src/Qwader.Api/appsettings.json)، ويظهر تحت حقوق الطبع. غيّره عند كل إصدار.

## الإنتاج (ملخص)

- `cd web && npm run build` يبني الواجهة داخل `src/Qwader.Api/wwwroot`، فيخدمها الخادم نفسه.
- الإعدادات عبر متغيرات البيئة:
  `ConnectionStrings__Default`، `Jwt__SigningKey`، `Seed__AdminPassword` (لأول تشغيل فقط).
- يجب تشغيله خلف HTTPS، فكوكي الجلسة يصبح `Secure` تلقائيًا.

## النشر

### الخادم (MonsterASP — `qwader.runasp.net`)
1. أنشئ الملف `src/Qwader.Api/appsettings.Production.json` (مستثنى من Git)، وفيه: سلسلة الاتصال، و`Jwt:SigningKey`، و`Seed:AdminPassword` (لأول تشغيل فقط).
2. ابنِ الواجهة ثم انشر:

```powershell
cd web; npm run build; cd ..
dotnet publish src/Qwader.Api -c Release -o publish-out
& 'C:\Program Files\IIS\Microsoft Web Deploy V3\msdeploy.exe' -verb:sync -source:contentPath=D:\Qwader\publish-out `
  "-dest:contentPath=site101811,computerName=https://site101811.siteasp.net:8172/msdeploy.axd?site=site101811,userName=site101811,password=<كلمة مرور النشر>,authType=Basic" `
  -allowUntrusted -enableRule:AppOffline -enableRule:DoNotDeleteRule
```

`DoNotDeleteRule` يمنع حذف الصور المرفوعة في `App_Data` عند كل نشر. قاعدة البيانات تُحدَّث تلقائيًا عند التشغيل.

### الواجهة (Cloudflare Worker `qwader` — https://qwader.ma8228354.workers.dev)
الـ Worker مربوط بالمستودع على GitHub، و**كل `git push` إلى `main` ينشر تلقائيًا**. الإعدادات كلها في [wrangler.jsonc](wrangler.jsonc):
- خطوة البناء تبني الواجهة إلى `web/dist`.
- الملفات الثابتة تُخدم مباشرة، مع الرجوع إلى `index.html` لروابط الصفحات.
- طلبات `/api/*` يمررها [web/worker/proxy.js](web/worker/proxy.js) إلى الخادم، فيبقى المتصفح على نطاق واحد آمن (HTTPS).
- كل زيارة على `http://` تُحوَّل إلى `https://` (308) مع HSTS، فلا تُرسل كلمة مرور بدون تشفير.

بعد تفعيل SSL على MonsterASP، غيّر `API_ORIGIN` في `wrangler.jsonc` إلى `https://qwader.runasp.net` وادفع التغيير.
للتجربة محليًا: `web/node_modules/.bin/wrangler dev` من جذر المستودع.

## ترحيلات قاعدة البيانات

```bash
dotnet ef migrations add <Name> -p src/Qwader.Infrastructure -s src/Qwader.Api -o Persistence/Migrations
dotnet ef database update      -p src/Qwader.Infrastructure -s src/Qwader.Api
```

## حالة المراحل

| المرحلة | الحالة |
|---|---|
| 1. التحليل والتصميم | ✅ |
| 2. الأساس: المصادقة والصلاحيات والمستخدمون والإعدادات والترقيم والتدقيق والواجهة الأساسية | ✅ |
| 3. الأصناف والمنتجات والمخزون والجرد والمشتريات والموردون والبحث الموحد | ✅ |
| 4. المبيعات ونقطة البيع والعملاء والمرتجعات وطباعة الفاتورة | ✅ |
| 5. الحسابات والحوالات والسندات والمصروفات وتحصيل الديون | ✅ |
| 6. التقارير ولوحة التحكم | ✅ |
| 7. الإشعارات على الجوال، تطبيق PWA، النسخ الاحتياطي والتصفير | ✅ |
| 8. الاختبار الشامل (آلي + 149 حالة يدوية) والنشر | ✅ |
