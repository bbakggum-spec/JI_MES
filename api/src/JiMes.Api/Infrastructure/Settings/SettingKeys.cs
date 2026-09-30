namespace JiMes.Api.Infrastructure.Settings;

/// <summary>
/// 코드에서 읽는 system_setting 키. 값은 DDL 초기 데이터에 있어야 한다 (테스트로 확인).
/// 새 설정은 DDL INSERT 추가 → 여기 상수 추가 순서로 넣는다.
/// </summary>
public static class SettingKeys
{
    public const string AuthSessionTimeoutMin = "auth.session_timeout_min";
    public const string AuthPermissionCacheSec = "auth.permission_cache_sec";
    public const string AuthLoginMaxAttemptsPerMin = "auth.login_max_attempts_per_min";
    public const string AuthPasswordMinLength = "auth.password_min_length";
}
