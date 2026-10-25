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
dotnet test        # ينشئ قاعدة Qwader_IntegrationTests ويحذفها عند كل تشغيل
cd web && npx tsc -b --noEmit
```

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

### الواجهة (Cloudflare Pages)
Workers & Pages ← Create ← Pages ← Connect to Git ← المستودع `Qwader`، ثم:

| الإعداد | القيمة |
|---|---|
| Framework preset | None |
| Root directory | `web` |
| Build command | `npm ci && npm run build:pages` |
| Build output directory | `dist` |
| Environment variables | `API_ORIGIN = http://qwader.runasp.net` و `NODE_VERSION = 22` |

الطلبات إلى `/api/*` تمر عبر `web/functions/api/[[path]].js` إلى الخادم، فيبقى المتصفح على نطاق واحد آمن (HTTPS).
بعد تفعيل SSL على MonsterASP، غيّر `API_ORIGIN` إلى `https://qwader.runasp.net`.

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
| 4. المبيعات ونقطة البيع والعملاء والمرتجعات | ⏳ التالية |
| 5. الحسابات والحوالات والسندات والمصروفات | — |
| 6. التقارير | — |
| 7–8. الصقل والاختبار والنشر | — |
