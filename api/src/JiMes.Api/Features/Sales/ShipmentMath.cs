namespace JiMes.Api.Features.Sales;

/// <summary>
/// 출하 금액 계산 (설계 §17 단가, 구 B4 결함 — 구 코드는 단위와 무관하게 항상 수량 × 단가) — DB 없는 순수 함수.
/// EA: 수량 × 단가, KG: 중량 × 단가, CHARGE: charge 수 × 단가. 시험편은 금액에 넣지 않는다.
/// </summary>
public static class ShipmentMath
{
    public static decimal? Amount(string priceBasis, decimal qty, decimal? weight, decimal? chargeCount, decimal? unitPrice)
    {
        if (unitPrice is not { } price) return null;
        return priceBasis switch
        {
            "KG" => weight is { } w ? w * price : null,
            "CHARGE" => chargeCount is { } c ? c * price : null,
            _ => qty * price,
        };
    }

    /// <summary>금액 반올림 = 설정 sales.amount_rounding (ROUND / FLOOR / CEIL), 원 단위</summary>
    public static decimal Round(decimal amount, string mode) => mode switch
    {
        "FLOOR" => Math.Floor(amount),
        "CEIL" => Math.Ceiling(amount),
        _ => Math.Round(amount, 0, MidpointRounding.AwayFromZero),
    };

    /// <summary>전표 합계 — 공급가액 = 행 금액 합(행마다 반올림), 세액 = 공급가액 × 세율 반올림</summary>
    public static (decimal Supply, decimal Vat, decimal Total) Totals(IEnumerable<decimal?> amounts, decimal vatRate, string mode)
    {
        var supply = amounts.Sum(a => a is { } v ? Round(v, mode) : 0);
        var vat = Round(supply * vatRate, mode);
        return (supply, vat, supply + vat);
    }

    /// <summary>업체 마감 기준일 — 마감일(1~31, 31 = 말일)이 그 달 날짜 수보다 크면 말일</summary>
    public static DateOnly ClosingDate(int year, int month, int? closingDay)
    {
        var last = DateTime.DaysInMonth(year, month);
        return new DateOnly(year, month, Math.Min(closingDay ?? last, last));
    }
}
