using ZXing;
using ZXing.Common;
using ZXing.Rendering;

namespace JiMes.Api.Features.Printing.Fixed;

/// <summary>
/// 바코드 SVG (구 ProcessSheet·ProductLabel 의 ZXing CODE_128 비트맵 → 서버 OS 와 무관한 벡터).
/// 옵션 barcode{format, width, height} — 크기는 출력 영역에 맞춰 늘어난다.
/// </summary>
public static class Barcodes
{
    public static string Svg(string? content, string format = "CODE_128", int width = 200, int height = 70)
    {
        var writer = new BarcodeWriterSvg
        {
            Format = Enum.TryParse<BarcodeFormat>(format, true, out var f) ? f : BarcodeFormat.CODE_128,
            Options = new EncodingOptions { Width = width, Height = height, Margin = 0, PureBarcode = false },
        };
        var svg = writer.Write(string.IsNullOrWhiteSpace(content) ? "N/A" : content).Content;
        // QuestPDF 가 크기를 정하도록 viewBox 를 둔다 (ZXing SVG 는 width/height 만 지정)
        return svg.Contains("viewBox") ? svg : svg.Replace("<svg ", $"<svg viewBox=\"0 0 {width} {height}\" ");
    }
}
