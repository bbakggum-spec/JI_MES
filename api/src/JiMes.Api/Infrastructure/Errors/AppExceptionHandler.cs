using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace JiMes.Api.Infrastructure.Errors;

public sealed class AppExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        if (exception is not AppException app)
            return false;   // 예상하지 못한 오류는 기본 처리기(500)로

        var problem = new ProblemDetails { Status = app.Status, Title = app.Message };
        problem.Extensions["code"] = app.Code;
        if (app is RequestValidationException validation)
            problem.Extensions["errors"] = validation.Errors;
        foreach (var (key, value) in app.Extra ?? new Dictionary<string, object?>())
            problem.Extensions[key] = value;

        context.Response.StatusCode = app.Status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = problem,
            Exception = exception,
        });
    }
}
