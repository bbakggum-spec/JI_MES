using MySqlConnector;

namespace JiMes.Api.Features.Printing.Providers;

/// <param name="PartId">양식 선택용 (품목(+업체) → 품목 공통 → 용도 기본)</param>
/// <param name="FileNameHint">발행 파일명에 붙일 식별자 (예: 검사번호-순번, 전표번호)</param>
public sealed record PrintSource(PrintData Data, long? PartId, long? CustomerId, string FileNameHint);

/// <summary>
/// 데이터 공급원 = print_data_source 1행에 대응하는 코드 (§5.3.1 "새 공급원은 개발 필요").
/// 사전(print_field)의 키로만 값을 채운다 (§15.2 P3).
/// </summary>
public interface IPrintDataProvider
{
    /// <summary>print_data_source.data_source_code</summary>
    string DataSourceCode { get; }

    /// <summary>발행 권한 = 이 메뉴 읽기 (화면에서 출력 — 설계 §12 ⑦)</summary>
    string MenuKey { get; }

    /// <summary>print_log.source_table</summary>
    string SourceTable { get; }

    /// <summary>구 좌표형 치환자 {{T1_3_P2}} 허용 여부</summary>
    bool AllowLegacyGridKeys => false;

    Task<PrintSource> LoadAsync(MySqlConnection conn, long sourceId, FieldDictionary fields, DateTime issuedAt, CancellationToken ct);

    /// <summary>발행 기록 후 원본에 반영할 것 (예: 성적서 발행 일시·횟수). print_log 와 같은 트랜잭션.</summary>
    Task OnIssuedAsync(MySqlConnection conn, MySqlTransaction tx, long sourceId, long printTemplateId, DateTime issuedAt) => Task.CompletedTask;
}
