namespace JiMes.Api.Infrastructure.Errors;

/// <summary>
/// 업무 오류. <see cref="AppExceptionHandler"/> 가 ProblemDetails 로 바꾼다.
/// 클라이언트는 <see cref="Code"/> 로 분기하고, 메시지는 그대로 표시만 한다.
/// </summary>
public abstract class AppException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

public sealed class NotFoundException(string table, long id)
    : AppException(StatusCodes.Status404NotFound, "NOT_FOUND", $"대상을 찾을 수 없습니다. ({table} #{id})");

/// <summary>row_version 불일치 — 다른 사용자가 먼저 수정함.</summary>
public sealed class ConcurrencyConflictException(string table, long id)
    : AppException(StatusCodes.Status409Conflict, "CONCURRENCY_CONFLICT",
        $"다른 사용자가 먼저 수정했습니다. 새로 고친 뒤 다시 시도하세요. ({table} #{id})");

public sealed class BusinessRuleException(string code, string message)
    : AppException(StatusCodes.Status422UnprocessableEntity, code, message);

public sealed class RequestValidationException(IDictionary<string, string[]> errors)
    : AppException(StatusCodes.Status400BadRequest, "VALIDATION", "입력값이 올바르지 않습니다.")
{
    public IDictionary<string, string[]> Errors { get; } = errors;

    public RequestValidationException(string field, string error)
        : this(new Dictionary<string, string[]> { [field] = [error] }) { }
}
