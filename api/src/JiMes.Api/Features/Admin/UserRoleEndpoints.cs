using Dapper;
using JiMes.Api.Infrastructure.Audit;
using JiMes.Api.Infrastructure.Data;
using JiMes.Api.Infrastructure.Errors;
using JiMes.Api.Infrastructure.Security;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace JiMes.Api.Features.Admin;

public sealed class UserDto
{
    public long AppUserId { get; init; }
    public string LoginId { get; init; } = "";
    public string UserName { get; init; } = "";
    public long? EmployeeId { get; init; }
    public string? EmployeeName { get; init; }
    public bool IsActive { get; init; }
    public DateTime? LastLoginAt { get; init; }
    public string? RoleIdsCsv { get; init; }
    public long[] RoleIds => string.IsNullOrEmpty(RoleIdsCsv) ? [] : RoleIdsCsv.Split(',').Select(long.Parse).ToArray();
}

public sealed class RoleDto
{
    public long RoleId { get; init; }
    public string RoleCode { get; init; } = "";
    public string RoleName { get; init; } = "";
    public string? Description { get; init; }
    public bool IsActive { get; init; }
    public long UserCount { get; init; }
    public bool IsLocked { get; set; }
}

public sealed class RoleMenuDto
{
    public long MenuId { get; init; }
    public string MenuKey { get; init; } = "";
    public string MenuName { get; init; } = "";
    public long? ParentMenuId { get; init; }
    public bool Read { get; init; }
    public bool Create { get; init; }
    public bool Update { get; init; }
    public bool Delete { get; init; }
}

public sealed record CreateUserRequest(string? LoginId, string? UserName, long? EmployeeId, string? Password, long[]? RoleIds);
public sealed record UpdateUserRequest(string? UserName, long? EmployeeId, bool IsActive, long[]? RoleIds, string? Reason);
public sealed record ResetPasswordRequest(string? NewPassword);
public sealed record RoleRequest(string? RoleCode, string? RoleName, string? Description, bool IsActive = true);
public sealed record MenuGrant(long MenuId, bool Read, bool Create, bool Update, bool Delete);
public sealed record RoleMenusRequest(MenuGrant[]? Grants, string? Reason);

/// <summary>
/// 사용자·역할·메뉴 권한 관리 (system.user / system.role). 변경하면 권한 캐시를 즉시 비운다 (설계 §18.3).
/// 잠금 방지: 자기 계정 사용 중지·자기 역할 변경 불가, 관리자 역할(Bootstrap:AdminRoleCode)은 권한·사용 여부 변경 불가.
/// </summary>
public static class UserRoleEndpoints
{
    public static void MapUserRoleEndpoints(this IEndpointRouteBuilder app)
    {
        var users = app.MapGroup("/api/users").WithTags("Users");
        users.MapGet("/", ListUsersAsync).RequirePermission(MenuKeys.SystemUser, PermissionAction.Read);
        users.MapGet("/role-options", async (IDbConnectionFactory db, CancellationToken ct) =>
            {
                await using var conn = await db.OpenAsync(ct);
                return Results.Ok(await conn.QueryAsync<RoleDto>("SELECT role_id, role_code, role_name, is_active FROM role ORDER BY role_code"));
            })
            .RequirePermission(MenuKeys.SystemUser, PermissionAction.Read);
        users.MapPost("/", CreateUserAsync).RequirePermission(MenuKeys.SystemUser, PermissionAction.Create);
        users.MapPut("/{id:long}", UpdateUserAsync).RequirePermission(MenuKeys.SystemUser, PermissionAction.Update);
        users.MapPost("/{id:long}/reset-password", ResetPasswordAsync).RequirePermission(MenuKeys.SystemUser, PermissionAction.Update);

        var roles = app.MapGroup("/api/roles").WithTags("Roles");
        roles.MapGet("/", ListRolesAsync).RequirePermission(MenuKeys.SystemRole, PermissionAction.Read);
        roles.MapPost("/", CreateRoleAsync).RequirePermission(MenuKeys.SystemRole, PermissionAction.Create);
        roles.MapPut("/{id:long}", UpdateRoleAsync).RequirePermission(MenuKeys.SystemRole, PermissionAction.Update);
        roles.MapGet("/{id:long}/menus", RoleMenusAsync).RequirePermission(MenuKeys.SystemRole, PermissionAction.Read);
        roles.MapPut("/{id:long}/menus", SaveRoleMenusAsync).RequirePermission(MenuKeys.SystemRole, PermissionAction.Update);
    }

    // ───────────────────────── 사용자 ─────────────────────────

    private const string UserSelect =
        """
        SELECT u.app_user_id, u.login_id, u.user_name, u.employee_id, e.employee_name, u.is_active, u.last_login_at,
               (SELECT GROUP_CONCAT(ur.role_id ORDER BY ur.role_id) FROM app_user_role ur WHERE ur.app_user_id = u.app_user_id) AS role_ids_csv
          FROM app_user u LEFT JOIN employee e ON e.employee_id = u.employee_id
        """;

    private static async Task<IResult> ListUsersAsync(IDbConnectionFactory db, CancellationToken ct, string? search = null, bool includeInactive = false)
    {
        await using var conn = await db.OpenAsync(ct);
        return Results.Ok(await conn.QueryAsync<UserDto>(
            UserSelect + """
             WHERE (@includeInactive OR u.is_active = 1)
               AND (@search IS NULL OR u.login_id LIKE @like OR u.user_name LIKE @like)
             ORDER BY u.login_id
            """, new { includeInactive, search = string.IsNullOrWhiteSpace(search) ? null : search, like = $"%{search?.Trim()}%" }));
    }

    private static async Task<IResult> CreateUserAsync(
        CreateUserRequest r, IDbConnectionFactory db, PasswordService passwords, AuditWriter audit, PermissionService permissions, CancellationToken ct)
    {
        var loginId = r.LoginId?.Trim();
        var userName = r.UserName?.Trim();
        if (string.IsNullOrEmpty(loginId) || loginId.Length > 50)
            throw new RequestValidationException("loginId", "아이디는 1~50자여야 합니다.");
        if (string.IsNullOrEmpty(userName) || userName.Length > 50)
            throw new RequestValidationException("userName", "이름은 1~50자여야 합니다.");
        passwords.EnsurePolicy("password", r.Password);

        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        if (await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM app_user WHERE login_id = @loginId", new { loginId }, tx) > 0)
            throw new RequestValidationException("loginId", "이미 사용 중인 아이디입니다.");
        var id = await conn.ExecuteScalarAsync<long>(
            "INSERT INTO app_user (login_id, password_hash, user_name, employee_id) VALUES (@loginId, @hash, @userName, @EmployeeId); SELECT LAST_INSERT_ID();",
            new { loginId, hash = passwords.Hash(r.Password!), userName, r.EmployeeId }, tx);
        var roleIds = await ReplaceRolesAsync(conn, tx, id, r.RoleIds ?? []);
        await audit.WriteAsync(conn, tx, AuditAction.Create, "app_user", id, null,
            new { login_id = loginId, user_name = userName, employee_id = r.EmployeeId, role_ids = roleIds });
        await tx.CommitAsync(ct);
        permissions.InvalidateAll();
        return Results.Ok(new { appUserId = id });
    }

    private static async Task<IResult> UpdateUserAsync(
        long id, UpdateUserRequest r, HttpContext http, IDbConnectionFactory db, AuditWriter audit, PermissionService permissions, CancellationToken ct)
    {
        var userName = r.UserName?.Trim();
        if (string.IsNullOrEmpty(userName) || userName.Length > 50)
            throw new RequestValidationException("userName", "이름은 1~50자여야 합니다.");

        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var before = await conn.QuerySingleOrDefaultAsync<UserDto>(UserSelect + " WHERE u.app_user_id = @id FOR UPDATE", new { id }, tx)
            ?? throw new NotFoundException("app_user", id);
        var self = http.User.GetUserId() == id;
        var newRoles = (r.RoleIds ?? []).Distinct().Order().ToArray();
        if (self && !r.IsActive)
            throw new BusinessRuleException("SELF_LOCKOUT", "자기 계정은 사용 중지할 수 없습니다.");
        if (self && !newRoles.SequenceEqual(before.RoleIds.Order()))
            throw new BusinessRuleException("SELF_LOCKOUT", "자기 계정의 역할은 바꿀 수 없습니다. 다른 관리자에게 요청하세요.");

        await conn.ExecuteAsync("UPDATE app_user SET user_name = @userName, employee_id = @EmployeeId, is_active = @IsActive WHERE app_user_id = @id",
            new { id, userName, r.EmployeeId, r.IsActive }, tx);
        await ReplaceRolesAsync(conn, tx, id, newRoles);
        await audit.WriteAsync(conn, tx, before.IsActive != r.IsActive ? AuditAction.StatusChange : AuditAction.Update, "app_user", id,
            new { user_name = before.UserName, employee_id = before.EmployeeId, is_active = before.IsActive, role_ids = before.RoleIds },
            new { user_name = userName, r.EmployeeId, is_active = r.IsActive, role_ids = newRoles }, r.Reason);
        await tx.CommitAsync(ct);
        permissions.InvalidateAll();
        return Results.NoContent();
    }

    private static async Task<IResult> ResetPasswordAsync(
        long id, ResetPasswordRequest r, IDbConnectionFactory db, PasswordService passwords, AuditWriter audit, CancellationToken ct)
    {
        passwords.EnsurePolicy("newPassword", r.NewPassword);
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        if (await conn.ExecuteAsync("UPDATE app_user SET password_hash = @hash WHERE app_user_id = @id",
                new { id, hash = passwords.Hash(r.NewPassword!) }, tx) == 0)
            throw new NotFoundException("app_user", id);
        await audit.WriteAsync(conn, tx, AuditAction.Update, "app_user", id, null, new { password_reset = true }, "관리자 비밀번호 초기화");
        await tx.CommitAsync(ct);
        return Results.NoContent();
    }

    private static async Task<long[]> ReplaceRolesAsync(MySqlConnection conn, MySqlTransaction tx, long userId, long[] roleIds)
    {
        var ids = roleIds.Distinct().Order().ToArray();
        if (ids.Length > 0 && await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM role WHERE role_id IN @ids", new { ids }, tx) != ids.Length)
            throw new RequestValidationException("roleIds", "없는 역할이 있습니다.");
        await conn.ExecuteAsync("DELETE FROM app_user_role WHERE app_user_id = @userId", new { userId }, tx);
        foreach (var roleId in ids)
            await conn.ExecuteAsync("INSERT INTO app_user_role (app_user_id, role_id) VALUES (@userId, @roleId)", new { userId, roleId }, tx);
        return ids;
    }

    // ───────────────────────── 역할 ─────────────────────────

    private static async Task<IResult> ListRolesAsync(IDbConnectionFactory db, IOptions<BootstrapOptions> bootstrap, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        var roles = (await conn.QueryAsync<RoleDto>(
            """
            SELECT r.role_id, r.role_code, r.role_name, r.description, r.is_active,
                   (SELECT COUNT(*) FROM app_user_role ur JOIN app_user u ON u.app_user_id = ur.app_user_id AND u.is_active = 1
                     WHERE ur.role_id = r.role_id) AS user_count
              FROM role r ORDER BY r.role_code
            """)).ToList();
        foreach (var role in roles)
            role.IsLocked = role.RoleCode == bootstrap.Value.AdminRoleCode;
        return Results.Ok(roles);
    }

    private static async Task<IResult> CreateRoleAsync(RoleRequest r, IDbConnectionFactory db, AuditWriter audit, CancellationToken ct)
    {
        var code = r.RoleCode?.Trim().ToUpperInvariant();
        var name = r.RoleName?.Trim();
        if (string.IsNullOrEmpty(code) || code.Length > 50 || !code.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
            throw new RequestValidationException("roleCode", "역할 코드는 영문·숫자·_ 1~50자입니다.");
        if (string.IsNullOrEmpty(name) || name.Length > 100)
            throw new RequestValidationException("roleName", "역할 이름은 1~100자여야 합니다.");

        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        if (await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM role WHERE role_code = @code", new { code }, tx) > 0)
            throw new RequestValidationException("roleCode", "이미 있는 역할 코드입니다.");
        var id = await conn.ExecuteScalarAsync<long>(
            "INSERT INTO role (role_code, role_name, description, is_active) VALUES (@code, @name, @Description, @IsActive); SELECT LAST_INSERT_ID();",
            new { code, name, r.Description, r.IsActive }, tx);
        await audit.WriteAsync(conn, tx, AuditAction.Create, "role", id, null, new { role_code = code, role_name = name, r.Description });
        await tx.CommitAsync(ct);
        return Results.Ok(new { roleId = id });
    }

    private static async Task<IResult> UpdateRoleAsync(
        long id, RoleRequest r, IDbConnectionFactory db, AuditWriter audit, PermissionService permissions, IOptions<BootstrapOptions> bootstrap, CancellationToken ct)
    {
        var name = r.RoleName?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 100)
            throw new RequestValidationException("roleName", "역할 이름은 1~100자여야 합니다.");
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var before = await conn.QuerySingleOrDefaultAsync<RoleDto>(
            "SELECT role_id, role_code, role_name, description, is_active FROM role WHERE role_id = @id FOR UPDATE", new { id }, tx)
            ?? throw new NotFoundException("role", id);
        if (before.RoleCode == bootstrap.Value.AdminRoleCode && !r.IsActive)
            throw new BusinessRuleException("ADMIN_ROLE_LOCKED", "관리자 역할은 사용 중지할 수 없습니다.");
        await conn.ExecuteAsync("UPDATE role SET role_name = @name, description = @Description, is_active = @IsActive WHERE role_id = @id",
            new { id, name, r.Description, r.IsActive }, tx);
        await audit.WriteAsync(conn, tx, AuditAction.Update, "role", id,
            new { before.RoleName, before.Description, before.IsActive }, new { role_name = name, r.Description, r.IsActive });
        await tx.CommitAsync(ct);
        permissions.InvalidateAll();
        return Results.NoContent();
    }

    private static async Task<IResult> RoleMenusAsync(long id, IDbConnectionFactory db, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        return Results.Ok(await conn.QueryAsync<RoleMenuDto>(
            """
            SELECT m.menu_id, m.menu_key, m.menu_name, m.parent_menu_id,
                   COALESCE(rm.can_read, 0) AS `read`, COALESCE(rm.can_create, 0) AS `create`,
                   COALESCE(rm.can_update, 0) AS `update`, COALESCE(rm.can_delete, 0) AS `delete`
              FROM menu m LEFT JOIN role_menu rm ON rm.menu_id = m.menu_id AND rm.role_id = @id
             WHERE m.is_active = 1
             ORDER BY COALESCE(m.parent_menu_id, m.menu_id), m.parent_menu_id IS NOT NULL, m.sort_order, m.menu_id
            """, new { id }));
    }

    private static async Task<IResult> SaveRoleMenusAsync(
        long id, RoleMenusRequest r, IDbConnectionFactory db, AuditWriter audit, PermissionService permissions,
        IOptions<BootstrapOptions> bootstrap, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var code = await conn.ExecuteScalarAsync<string?>("SELECT role_code FROM role WHERE role_id = @id FOR UPDATE", new { id }, tx)
            ?? throw new NotFoundException("role", id);
        if (code == bootstrap.Value.AdminRoleCode)
            throw new BusinessRuleException("ADMIN_ROLE_LOCKED", "관리자 역할은 모든 메뉴 전체 권한으로 고정입니다 (DDL).");

        var before = (await conn.QueryAsync<RoleMenuDto>(
            """
            SELECT m.menu_id, m.menu_key, rm.can_read AS `read`, rm.can_create AS `create`, rm.can_update AS `update`, rm.can_delete AS `delete`
              FROM role_menu rm JOIN menu m ON m.menu_id = rm.menu_id WHERE rm.role_id = @id
            """, new { id }, tx)).ToList();
        // 읽기 없이 쓰기만 주는 것은 의미가 없으므로 쓰기 권한이 있으면 읽기도 준다
        var grants = (r.Grants ?? []).Where(g => g.Read || g.Create || g.Update || g.Delete).ToList();
        await conn.ExecuteAsync("DELETE FROM role_menu WHERE role_id = @id", new { id }, tx);
        foreach (var g in grants)
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO role_menu (role_id, menu_id, can_read, can_create, can_update, can_delete)
                SELECT @id, menu_id, 1, @Create, @Update, @Delete FROM menu WHERE menu_id = @MenuId
                """, new { id, g.MenuId, g.Create, g.Update, g.Delete }, tx);
        }
        await audit.WriteAsync(conn, tx, AuditAction.Update, "role", id,
            new { menus = before.Select(b => new { b.MenuKey, b.Read, b.Create, b.Update, b.Delete }) },
            new { menus = grants }, r.Reason ?? "메뉴 권한 변경");
        await tx.CommitAsync(ct);
        permissions.InvalidateAll();
        return Results.NoContent();
    }
}
