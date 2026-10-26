# Manual test cases

`cases.py` holds every manual test case; `build.py` turns it into `docs/Qwader-Test-Cases.xlsx`.

```
python tools/test-cases/build.py docs/Qwader-Test-Cases.xlsx
```

The workbook has no cached formula values until opened in Excel (or recalculated).

## Running the sheet against the screens

`ui-run/` drives the real screens with Playwright (Chrome) against a **local QA copy** — never the live site:
it creates users, sales and finally runs both «تصفير النظام» cases.

```
# API on a throwaway database, login rate limit raised for the run
ASPNETCORE_ENVIRONMENT=Development RateLimiting__LoginPerMinute=10000 \
ConnectionStrings__Default="Server=.;Database=Qwader_QA;Trusted_Connection=True;TrustServerCertificate=True" \
Seed__AdminPassword=Admin12345 Seed__SkipPasswordChange=true dotnet run --project src/Qwader.Api --launch-profile http

node tools/test-cases/ui-run/run.cjs            # all cases → results.json + shots-final/
node tools/test-cases/ui-run/run.cjs --keep --only=POS-,SAL-03
```
