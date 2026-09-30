using System.Text;
using System.Text.RegularExpressions;
using JiMes.Api.Features.Printing;
using JiMes.Api.Features.Printing.Fixed;

namespace JiMes.Api.Tests.Printing;

public sealed class SalesSlipRendererTests
{
    static SalesSlipRendererTests()
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        QuestPDF.Settings.UseSystemFonts = true;   // 개발 PC 설치 글꼴 (굴림체)
    }

    [Fact]
    public void Missing_fonts_fail_with_clear_code()
    {
        var ex = Assert.Throws<JiMes.Api.Infrastructure.Errors.BusinessRuleException>(() =>
            new SalesSlipRenderer().Render(Slip(1), """{"font": {"family": ["없는글꼴"]}}"""));
        Assert.Equal("FONT_NOT_INSTALLED", ex.Code);
    }

    private static PrintData Slip(int items)
    {
        var data = new PrintData();
        data.Values["ShipmentNo"] = "O260930-001";
        data.Values["ShipmentDate"] = "2026년 9월 30일";
        data.Values["SupplierName"] = "공급사";
        data.Values["CustomerName"] = "거래처";
        data.Values["SupplyAmount"] = 1000m;
        data.Values["VatAmount"] = 100m;
        data.Values["TotalAmount"] = 1100m;
        data.Lists["Items"] = Enumerable.Range(1, items).Select(i =>
        {
            var row = PrintData.Row();
            row["No"] = (decimal)i;
            row["PartName"] = $"품목{i}";
            row["Qty"] = 10m;
            row["Amount"] = 100m;
            return row;
        }).ToList();
        data.Images["Stamp"] = ExcelTemplateRendererTests.Png1x1;
        return data;
    }

    private static int PageCount(byte[] pdf) =>
        Regex.Matches(Encoding.Latin1.GetString(pdf), @"/Type\s*/Page(?![s\w])").Count;

    [Theory]
    [InlineData(0, 1)]
    [InlineData(6, 1)]
    [InlineData(7, 2)]    // 구 ITEMS_PER_PAGE = 6
    [InlineData(13, 3)]
    public void Pages_follow_items_per_page_option(int items, int pages)
    {
        var pdf = new SalesSlipRenderer().Render(Slip(items), """{"items_per_page": 6}""");
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
        Assert.Equal(pages, PageCount(pdf));
    }

    [Fact]
    public void Options_change_layout_and_missing_options_use_defaults()
    {
        var three = new SalesSlipRenderer().Render(Slip(7), """{"items_per_page": 3, "copies": [{"label": "(보관용)", "border_color": "#000000"}]}""");
        Assert.Equal(3, PageCount(three));
        var defaults = new SalesSlipRenderer().Render(Slip(7), null);
        Assert.Equal(2, PageCount(defaults));
    }
}
