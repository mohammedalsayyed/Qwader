using FluentAssertions;
using Qwader.Application.Abstractions;
using Qwader.Application.Common;
using Qwader.Domain.Administration;

namespace Qwader.UnitTests;

public sealed class ArabicTextTests
{
    [Theory]
    [InlineData("أحمد", "احمد")]
    [InlineData("إبريق", "ابريق")]
    [InlineData("آلة", "اله")]
    [InlineData("صحون زجاجيّة", "صحون زجاجيه")]
    [InlineData("مكنسة كهربائية", "مكنسه كهرباييه")]
    [InlineData("مستشفى", "مستشفي")]
    [InlineData("  صحن   كبير  ", "صحن كبير")]
    [InlineData("كــبــيــر", "كبير")]
    [InlineData("ABC-12", "abc-12")]
    [InlineData("٠٥٩٩١٢٣٤٥٦", "0599123456")]
    public void Normalize_unifies_spelling_variants(string input, string expected) =>
        ArabicText.Normalize(input).Should().Be(expected);

    [Fact]
    public void Normalize_of_blank_is_empty() => ArabicText.Normalize("   ").Should().BeEmpty();
}

public sealed class DocumentNumberFormatTests
{
    [Fact]
    public void Yearly_documents_include_the_year() =>
        DocumentNumbers.Format(DocumentType.SaleInvoice, 2026, 42).Should().Be("INV-2026-000042");

    [Fact]
    public void Master_data_codes_do_not_include_the_year() =>
        DocumentNumbers.Format(DocumentType.Customer, 2026, 7).Should().Be("C-00007");

    [Fact]
    public void Every_document_type_has_a_prefix() =>
        Enum.GetValues<DocumentType>().Should().AllSatisfy(t => DocumentNumbers.Prefix(t).Should().NotBeNullOrEmpty());
}

public sealed class LocalDayRangeTests
{
    private sealed class PalestineClock : IClock
    {
        public DateTime UtcNow { get; init; } = DateTime.UtcNow;
        public TimeZoneInfo LocalZone { get; } = FindZone();

        private static TimeZoneInfo FindZone()
        {
            foreach (var id in new[] { "Asia/Hebron", "West Bank Standard Time" })
                try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch (TimeZoneNotFoundException) { }
            throw new InvalidOperationException("Palestine time zone missing");
        }
    }

    [Fact]
    public void Summer_day_starts_at_21_00_utc_of_the_previous_day()
    {
        IClock clock = new PalestineClock();

        var (start, end) = clock.LocalDaysToUtcRange(new DateOnly(2026, 7, 15), new DateOnly(2026, 7, 15));

        start.Should().Be(new DateTime(2026, 7, 14, 21, 0, 0));
        end.Should().Be(new DateTime(2026, 7, 15, 21, 0, 0));
    }

    [Fact]
    public void Winter_day_starts_at_22_00_utc_of_the_previous_day()
    {
        IClock clock = new PalestineClock();

        var (start, _) = clock.LocalDaysToUtcRange(new DateOnly(2026, 1, 10), new DateOnly(2026, 1, 10));

        start.Should().Be(new DateTime(2026, 1, 9, 22, 0, 0));
    }

    [Fact]
    public void A_sale_at_23_30_local_belongs_to_that_local_day()
    {
        // 23:30 on 15 July in Palestine (UTC+3) is 20:30 UTC.
        IClock clock = new PalestineClock { UtcNow = new DateTime(2026, 7, 15, 20, 30, 0, DateTimeKind.Utc) };

        clock.LocalToday.Should().Be(new DateOnly(2026, 7, 15));
    }
}
