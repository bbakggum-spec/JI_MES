using Dapper;
using JiMes.Api.Infrastructure.Audit;
using JiMes.Api.Infrastructure.Data;
using Microsoft.Extensions.Options;

namespace JiMes.Api.Infrastructure.Security;

/// <summary>appsettings "Bootstrap". 비밀번호는 파일에 두지 않고 환경변수 Bootstrap__AdminPassword 로 준다.</summary>
public sealed class BootstrapOptions
{
    public const string Section = "Bootstrap";

    public string? AdminLoginId { get; set; }
    public string? AdminUserName { get; set; }
    public string? AdminRoleCode { get; set; }
    public string? AdminPassword { get; set; }
}

/// <summary>
/// app_user 가 한 건도 없을 때만 최초 관리자 계정을 만든다 (DDL 에 계정·비밀번호를 넣지 않기 위함).
/// 이미 사용자가 있으면 아무것도 하지 않는다.
/// </summary>
public sealed class AdminBootstrapper(
    IDbConnectionFactory db, PasswordService passwords, AuditWriter audit,
    IOptions<BootstrapOptions> options, ILogger<AdminBootstrapper> logger)
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        if (await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM app_user") > 0)
            return;

        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.AdminLoginId) || string.IsNullOrWhiteSpace(o.AdminRoleCode)
            || string.IsNullOrEmpty(o.AdminPassword))
        {
            logger.LogWarning("사용자가 없습니다. Bootstrap:AdminLoginId / AdminRoleCode / AdminPassword 를 설정하면 최초 관리자를 만듭니다.");
            return;
        }
        passwords.EnsurePolicy(nameof(o.AdminPassword), o.AdminPassword);

        await using var tx = await conn.BeginTransactionAsync(ct);
        var roleId = await conn.ExecuteScalarAsync<long?>(
            "SELECT role_id FROM role WHERE role_code = @code AND is_active = 1", new { code = o.AdminRoleCode }, tx)
            ?? throw new InvalidOperationException($"관리자 역할 '{o.AdminRoleCode}' 이 없습니다. DDL 권한 초기 데이터를 확인하세요.");

        var userName = string.IsNullOrWhiteSpace(o.AdminUserName) ? o.AdminLoginId : o.AdminUserName;
        var userId = await conn.ExecuteScalarAsync<long>(
            """
            INSERT INTO app_user (login_id, password_hash, user_name) VALUES (@login, @hash, @name);
            SELECT LAST_INSERT_ID();
            """,
            new { login = o.AdminLoginId, hash = passwords.Hash(o.AdminPassword), name = userName }, tx);
        await conn.ExecuteAsync(
            "INSERT INTO app_user_role (app_user_id, role_id) VALUES (@userId, @roleId)", new { userId, roleId }, tx);
        await audit.WriteAsync(conn, tx, AuditAction.Create, "app_user", userId, null,
            new { login_id = o.AdminLoginId, user_name = userName, role_codes = new[] { o.AdminRoleCode } },
            "최초 관리자 생성 (Bootstrap)");
        await tx.CommitAsync(ct);

        logger.LogWarning("최초 관리자 '{LoginId}' 를 만들었습니다. 로그인 후 비밀번호를 바꾸세요.", o.AdminLoginId);
    }
}
