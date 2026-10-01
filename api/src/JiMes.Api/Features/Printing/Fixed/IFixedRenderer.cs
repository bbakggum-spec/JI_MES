using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace JiMes.Api.Features.Printing.Fixed;

/// <summary>
/// FIXED 양식 렌더러 (print_template.renderer_key). 레이아웃 구조는 코드, 수치·문구는 옵션 JSON (§15.3.1).
/// 새 고정 양식 = 이 인터페이스 구현 추가 + DI 등록 + DDL 양식·옵션 초기값.
/// 페이지는 받은 문서에 덧붙인다 → 여러 건 발행(공정이동표 여러 장 등)은 한 PDF 로 이어진다.
/// </summary>
public interface IFixedRenderer
{
    string RendererKey { get; }

    void Compose(IDocumentContainer document, PrintData data, LayoutOptions options);

    /// <returns>PDF (1건)</returns>
    byte[] Render(PrintData data, string? optionsJson) => RenderMany([data], optionsJson);

    /// <returns>PDF (여러 건을 순서대로 이어 붙임)</returns>
    byte[] RenderMany(IReadOnlyList<PrintData> items, string? optionsJson)
    {
        var options = LayoutOptions.Parse(optionsJson);
        return Document.Create(doc => { foreach (var data in items) Compose(doc, data, options); }).GeneratePdf();
    }
}
