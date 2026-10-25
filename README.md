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
