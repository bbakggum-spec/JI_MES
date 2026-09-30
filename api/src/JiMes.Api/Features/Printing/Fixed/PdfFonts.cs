using JiMes.Api.Infrastructure.Errors;
using QuestPDF.Drawing;

namespace JiMes.Api.Features.Printing.Fixed;

/// <summary>
/// 고정 양식 글꼴 선택 (§15.3 F6). 옵션 font.family 는 이름 하나 또는 대체 순서 목록 — 설치된 것만 넘긴다
/// (QuestPDF 는 목록 중 하나라도 없으면 실패). 이름은 글꼴 파일에 기록된 이름 (한국어 Windows: "굴림체", "맑은 고딕").
/// 서버 글꼴 사용은 설정 Print:UseSystemFonts, 앱과 함께 배포하는 글꼴은 실행 폴더에 두면 자동 등록된다.
/// </summary>
public static class PdfFonts
{
    private static readonly Lazy<HashSet<string>> Available = new(() =>
    {
        var fonts = FontManager.GetRegisteredFonts().AsEnumerable();
        if (QuestPDF.Settings.UseSystemFonts)
            fonts = fonts.Concat(FontManager.GetSystemFonts());
        return fonts.Select(f => f.FamilyName).ToHashSet(StringComparer.OrdinalIgnoreCase);
    });

    public static string[] Pick(IReadOnlyList<string> families)
    {
        var picked = families.Where(Available.Value.Contains).ToArray();
        return picked.Length > 0
            ? picked
            : throw new BusinessRuleException("FONT_NOT_INSTALLED",
                $"출력 글꼴({string.Join(", ", families)})이 서버에 없습니다. 글꼴을 설치하거나 양식 옵션 font.family 를 바꾸세요.");
    }
}
