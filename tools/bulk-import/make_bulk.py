# -*- coding: utf-8 -*-
"""1000 categories × 4 products = 4000 rows in the product-import format (categories are created by the import)."""
import random
import sys
from openpyxl import Workbook
from openpyxl.styles import Alignment, Font, PatternFill

random.seed(2026)
OUT = sys.argv[1]

# (plural for the category, singular for the product, unit, price range for the purchase price)
DOMAINS = {
    'أجهزة كهربائية': (['تورنيدو', 'فيليبس', 'براون', 'مولينكس', 'كينوود', 'تيفال', 'بلاك آند ديكر', 'سامسونج', 'إل جي', 'زانوسي'], [
        ('خلاطات', 'خلاط', 'قطعة', (60, 250)), ('عصارات', 'عصارة', 'قطعة', (50, 220)), ('محضرات طعام', 'محضر طعام', 'قطعة', (150, 600)),
        ('قلايات هوائية', 'قلاية هوائية', 'قطعة', (180, 700)), ('أفران كهربائية', 'فرن كهربائي', 'قطعة', (250, 900)), ('ميكروويف', 'ميكروويف', 'قطعة', (250, 800)),
        ('غلايات', 'غلاية', 'قطعة', (35, 150)), ('ماكينات قهوة', 'ماكينة قهوة', 'قطعة', (150, 1200)), ('محمصات خبز', 'محمصة خبز', 'قطعة', (60, 200)),
        ('شوايات كهربائية', 'شواية كهربائية', 'قطعة', (90, 350)), ('مكاوي', 'مكواة', 'قطعة', (50, 300)), ('مكانس كهربائية', 'مكنسة كهربائية', 'قطعة', (200, 1100)),
        ('غسالات', 'غسالة', 'قطعة', (900, 2800)), ('ثلاجات', 'ثلاجة', 'قطعة', (1200, 4500)), ('فريزرات', 'فريزر', 'قطعة', (900, 2800)),
        ('مجففات ملابس', 'مجفف ملابس', 'قطعة', (900, 2500)), ('جلايات', 'جلاية', 'قطعة', (1100, 3000)), ('مراوح', 'مروحة', 'قطعة', (60, 300)),
        ('مكيفات', 'مكيف', 'قطعة', (1300, 3800)), ('دفايات', 'دفاية', 'قطعة', (80, 450)), ('سخانات مياه', 'سخان مياه', 'قطعة', (350, 1200)),
        ('منقيات هواء', 'منقي هواء', 'قطعة', (250, 1000)), ('مرطبات هواء', 'مرطب هواء', 'قطعة', (70, 300)), ('مكانس بخار', 'مكنسة بخار', 'قطعة', (200, 700)),
        ('ماكينات خياطة', 'ماكينة خياطة', 'قطعة', (300, 1100)), ('مجففات شعر', 'مجفف شعر', 'قطعة', (40, 250)), ('مكاوي شعر', 'مكواة شعر', 'قطعة', (40, 220)),
        ('ماكينات حلاقة', 'ماكينة حلاقة', 'قطعة', (40, 350)), ('موازين', 'ميزان', 'قطعة', (25, 150)), ('فرامات لحمة', 'فرامة لحمة', 'قطعة', (150, 600)),
    ]),
    'أدوات مطبخ': (['تيفال', 'بايركس', 'لومينارك', 'زوليكس', 'ديليمانو', 'كوركماز', 'لاف', 'تايجر', 'إيكيا', 'لوكال'], [
        ('طناجر', 'طنجرة', 'قطعة', (30, 180)), ('مقالي', 'مقلاة', 'قطعة', (25, 140)), ('صواني فرن', 'صينية فرن', 'قطعة', (15, 80)),
        ('أطقم سفرة', 'طقم سفرة', 'طقم', (80, 450)), ('صحون', 'صحن', 'قطعة', (3, 25)), ('كاسات', 'كاسة', 'قطعة', (2, 15)),
        ('فناجين', 'فنجان', 'قطعة', (3, 20)), ('ملاعق', 'ملعقة', 'دزينة', (10, 60)), ('شوك', 'شوكة', 'دزينة', (10, 60)),
        ('سكاكين', 'سكين', 'قطعة', (8, 70)), ('ألواح تقطيع', 'لوح تقطيع', 'قطعة', (8, 45)), ('مصافي', 'مصفاة', 'قطعة', (5, 35)),
        ('أواني تخزين', 'آنية تخزين', 'قطعة', (6, 40)), ('مطربانات', 'مطربان', 'قطعة', (3, 20)), ('ترامس', 'ترمس', 'قطعة', (20, 120)),
        ('أباريق', 'إبريق', 'قطعة', (10, 70)), ('صواني تقديم', 'صينية تقديم', 'قطعة', (12, 90)), ('علب بهارات', 'علبة بهارات', 'طقم', (15, 80)),
        ('حافظات طعام', 'حافظة طعام', 'قطعة', (10, 60)), ('أدوات خبز', 'أداة خبز', 'قطعة', (5, 45)),
    ]),
    'أدوات تنظيف': (['فيليدا', 'كلوركس', 'سانيتا', 'هوم كير', 'برايت', 'كلين', 'سوبر كلين', 'ماجيك', 'فريش', 'سبيدي'], [
        ('مماسح', 'ممسحة', 'قطعة', (10, 60)), ('مكانس يدوية', 'مكنسة يدوية', 'قطعة', (8, 45)), ('دلاء', 'دلو', 'قطعة', (6, 40)),
        ('منظفات', 'منظف', 'قطعة', (5, 35)), ('إسفنج', 'إسفنجة', 'علبة', (3, 20)), ('قفازات', 'قفاز', 'علبة', (4, 25)),
        ('سلال غسيل', 'سلة غسيل', 'قطعة', (12, 60)), ('حبال غسيل', 'حبل غسيل', 'قطعة', (5, 25)), ('مناشر', 'منشر', 'قطعة', (25, 150)),
        ('شماعات', 'شماعة', 'دزينة', (8, 45)),
    ]),
    'عدد وأدوات': (['بوش', 'ماكيتا', 'ستانلي', 'توتال', 'إنجكو', 'ديوالت', 'بلاك آند ديكر', 'كروان', 'تولسن', 'هيونداي'], [
        ('مفكات', 'مفك', 'قطعة', (5, 60)), ('مطارق', 'مطرقة', 'قطعة', (12, 70)), ('زراديات', 'زرادية', 'قطعة', (10, 60)),
        ('مفاتيح ربط', 'مفتاح ربط', 'قطعة', (8, 80)), ('مثاقب', 'مثقاب', 'قطعة', (120, 650)), ('صواريخ قص', 'صاروخ قص', 'قطعة', (110, 550)),
        ('مناشير', 'منشار', 'قطعة', (20, 400)), ('شرائط قياس', 'شريط قياس', 'قطعة', (6, 40)), ('موازين ماء', 'ميزان ماء', 'قطعة', (10, 70)),
        ('مسدسات سيليكون', 'مسدس سيليكون', 'قطعة', (12, 60)), ('سلالم', 'سلم', 'قطعة', (90, 450)), ('صناديق عدة', 'صندوق عدة', 'قطعة', (30, 250)),
        ('أقفال', 'قفل', 'قطعة', (10, 90)), ('مفصلات', 'مفصلة', 'قطعة', (3, 25)), ('براغي', 'برغي', 'علبة', (5, 35)),
        ('مسامير', 'مسمار', 'علبة', (4, 30)), ('لواصق', 'لاصق', 'قطعة', (3, 25)), ('دهانات', 'دهان', 'قطعة', (25, 220)),
        ('فراشي دهان', 'فرشاة دهان', 'قطعة', (3, 25)), ('رولات دهان', 'رول دهان', 'قطعة', (6, 40)),
    ]),
    'كهربائيات': (['فيليبس', 'أوسرام', 'شنايدر', 'ليجراند', 'باناسونيك', 'دوراسيل', 'إنرجايزر', 'أنكر', 'تي بي لينك', 'سوني'], [
        ('لمبات', 'لمبة', 'قطعة', (4, 35)), ('أسلاك كهرباء', 'سلك كهرباء', 'متر', (1, 6)), ('مشتركات كهرباء', 'مشترك كهرباء', 'قطعة', (12, 70)),
        ('أفياش', 'فيش', 'قطعة', (3, 20)), ('مفاتيح كهرباء', 'مفتاح كهرباء', 'قطعة', (4, 30)), ('كشافات', 'كشاف', 'قطعة', (15, 120)),
        ('بطاريات', 'بطارية', 'علبة', (5, 45)), ('شواحن', 'شاحن', 'قطعة', (15, 120)), ('وصلات تمديد', 'وصلة تمديد', 'قطعة', (10, 60)),
        ('مصابيح طاولة', 'مصباح طاولة', 'قطعة', (25, 150)),
    ]),
    'حديقة وحمامات': (['جروهي', 'هانزجروهي', 'كوهلر', 'جاردينا', 'ستار بلاست', 'كيتر', 'روكا', 'إيديال', 'تيكو', 'أكوا'], [
        ('خراطيم مياه', 'خرطوم مياه', 'قطعة', (20, 120)), ('رشاشات حديقة', 'رشاش حديقة', 'قطعة', (8, 60)), ('أدوات زراعة', 'أداة زراعة', 'قطعة', (6, 50)),
        ('أصص زرع', 'أصيص زرع', 'قطعة', (5, 60)), ('دشات', 'دش', 'قطعة', (30, 250)), ('حنفيات', 'حنفية', 'قطعة', (35, 300)),
        ('مرايا حمام', 'مرآة حمام', 'قطعة', (40, 250)), ('رفوف حمام', 'رف حمام', 'قطعة', (15, 90)), ('ستائر حمام', 'ستارة حمام', 'قطعة', (12, 60)),
        ('سجاد حمام', 'سجادة حمام', 'قطعة', (10, 60)),
    ]),
}
# Inanimate plurals take feminine-singular adjectives, so every qualifier reads correctly after every category name.
QUALIFIERS = ['منزلية', 'احترافية', 'صغيرة', 'كبيرة', 'اقتصادية', 'فاخرة', 'ستانلس', 'بلاستيكية', 'مستوردة', 'محلية']
SIZES = {'قطعة': ['صغير', 'وسط', 'كبير', '20 سم', '30 سم', '1 لتر', '2 لتر', '1500 واط', '2000 واط', ''], 'طقم': ['6 أشخاص', '12 شخص', '24 قطعة', ''],
         'دزينة': ['12 قطعة', ''], 'علبة': ['10 قطع', '50 قطعة', '100 قطعة', ''], 'متر': ['1.5 مم', '2.5 مم', '4 مم']}
COLORS = ['أبيض', 'أسود', 'فضي', 'أحمر', 'أزرق', 'رمادي', 'ذهبي', 'بيج', '']
QTY = {'قطعة': (0, 40), 'طقم': (0, 15), 'دزينة': (0, 30), 'علبة': (0, 60), 'متر': (0, 500)}


def ean13(body12: str) -> str:
    s = sum(int(d) * (3 if i % 2 else 1) for i, d in enumerate(body12))
    return body12 + str((10 - s % 10) % 10)


def half(x: float) -> float:
    return round(x * 2) / 2  # nearest half shekel


rows, categories = [], []
seq = 0
for domain, (brands, types) in DOMAINS.items():
    for plural, singular, unit, (lo, hi) in types:
        for q in QUALIFIERS:
            cat = f'{plural} {q}'
            categories.append((cat, domain))
            for _ in range(4):
                seq += 1
                cost = half(random.uniform(lo, hi))
                cost = max(cost, 0.5)
                wholesale = half(cost * random.uniform(1.12, 1.2))
                retail = half(cost * random.uniform(1.25, 1.45))
                retail = max(retail, wholesale + 0.5)
                min_price = half(cost * 1.05)
                brand = random.choice(brands)
                model = f'{random.choice("ABCDEKMPRSTX")}{random.choice("ABCDEKMPRSTX")}-{random.randint(100, 9999)}'
                size = random.choice(SIZES[unit])
                color = random.choice(COLORS) if unit != 'متر' else random.choice(['أحمر', 'أسود', 'أزرق', 'أصفر'])
                qlo, qhi = QTY[unit]
                # A shop keeps a few fridges but dozens of plates: fewer units the dearer the item.
                if cost >= 800: qhi = 4
                elif cost >= 250: qhi = 8
                elif cost >= 80: qhi = min(qhi, 15)
                qty = 0 if random.random() < 0.08 else random.randint(max(qlo, 1), qhi)
                reorder = max(1, round(qhi * random.uniform(0.05, 0.15)))
                name = f'{singular} {brand} {model}'
                barcode = ean13('200' + f'{seq:09d}')  # 200–299: in-store range, never clashes with factory barcodes
                rows.append(['', barcode, name, cat, unit, brand, size, color, model, cost, wholesale, retail, min_price, reorder, qty])

assert len(categories) == 1000 and len({c for c, _ in categories}) == 1000, len(categories)
assert len(rows) == 4000 and len({r[1] for r in rows}) == 4000

headers = ['الكود', 'الباركود', 'اسم المنتج', 'الصنف', 'الوحدة', 'الماركة', 'المقاس', 'اللون', 'الموديل',
           'سعر الشراء', 'سعر الجملة', 'سعر المفرق', 'أقل سعر بيع', 'حد التنبيه', 'الكمية الافتتاحية']
widths = [10, 16, 34, 26, 9, 14, 11, 9, 11, 11, 11, 11, 11, 10, 14]

wb = Workbook()
ws = wb.active
ws.title = 'المنتجات'  # the import reads the first sheet
ws.sheet_view.rightToLeft = True
head_font = Font(name='Arial', bold=True, color='FFFFFF')
fill = PatternFill('solid', fgColor='0F5F5A')
for i, (h, w) in enumerate(zip(headers, widths), start=1):
    c = ws.cell(row=1, column=i, value=h)
    c.font, c.fill, c.alignment = head_font, fill, Alignment(horizontal='center')
    ws.column_dimensions[c.column_letter].width = w
body = Font(name='Arial', size=10)
for r in rows:
    ws.append(r)
for row in ws.iter_rows(min_row=2):
    for c in row:
        c.font = body
    row[1].number_format = '@'  # barcode stays text (no 2.00E+12)
ws.freeze_panes = 'C2'
ws.auto_filter.ref = f'A1:O{len(rows) + 1}'

cs = wb.create_sheet('الأصناف (للاطلاع)')
cs.sheet_view.rightToLeft = True
cs.append(['#', 'الصنف', 'المجموعة', 'عدد المنتجات'])
for i, (cat, domain) in enumerate(categories, start=1):
    cs.append([i, cat, domain, 4])
for c in cs[1]:
    c.font, c.fill = head_font, fill
cs.column_dimensions['B'].width = 30
cs.column_dimensions['C'].width = 18
cs.column_dimensions['D'].width = 13
for row in cs.iter_rows(min_row=2):
    for c in row:
        c.font = body

wb.save(OUT)
print('saved', OUT, len(categories), 'categories,', len(rows), 'products, stock value', round(sum(r[9] * r[14] for r in rows)))

# Four parts of 1000 products (250 categories each): each import finishes well inside the hosting's request time limit.
base = OUT[:-5]
for part in range(4):
    pw = Workbook()
    ps = pw.active
    ps.title = 'المنتجات'
    ps.sheet_view.rightToLeft = True
    for i, (h, w) in enumerate(zip(headers, widths), start=1):
        c = ps.cell(row=1, column=i, value=h)
        c.font, c.fill, c.alignment = head_font, fill, Alignment(horizontal='center')
        ps.column_dimensions[c.column_letter].width = w
    for r in rows[part * 1000:(part + 1) * 1000]:
        ps.append(r)
    for row in ps.iter_rows(min_row=2):
        for c in row:
            c.font = body
        row[1].number_format = '@'
    ps.freeze_panes = 'C2'
    pw.save(f'{base}-جزء{part + 1}.xlsx')
print('parts saved')
