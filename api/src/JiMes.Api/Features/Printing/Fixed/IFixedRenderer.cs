namespace JiMes.Api.Features.Printing.Fixed;

/// <summary>
/// FIXED 양식 렌더러 (print_template.renderer_key). 레이아웃 구조는 코드, 수치·문구는 옵션 JSON (§15.3.1).
/// 새 고정 양식 = 이 인터페이스 구현 추가 + DI 등록 + DDL 양식·옵션 초기값.
/// </summary>
public interface IFixedRenderer
{
    string RendererKey { get; }

    /// <returns>PDF</returns>
    byte[] Render(PrintData data, string? optionsJson);
}
