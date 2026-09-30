using JiMes.Api.Infrastructure.Errors;
using JiMes.Api.Infrastructure.Settings;
using Microsoft.AspNetCore.Identity;

namespace JiMes.Api.Infrastructure.Security;

/// <summary>ASP.NET Core Identity 의 PBKDF2 해시를 쓴다 (app_user.password_hash).</summary>
public sealed class PasswordService(SettingsCache settings)
{
    private sealed class HashUser;

    private static readonly PasswordHasher<HashUser> Hasher = new();
    private static readonly HashUser User = new();

    // 없는 계정으로 로그인할 때도 같은 시간만큼 검증해 계정 존재 여부가 드러나지 않게 한다.
    private static readonly string DummyHash = Hasher.HashPassword(User, Guid.NewGuid().ToString());

    public string Hash(string password) => Hasher.HashPassword(User, password);

    /// <returns>(일치 여부, 재해시 필요 여부)</returns>
    public (bool Ok, bool NeedsRehash) Verify(string? hash, string password)
    {
        var result = Hasher.VerifyHashedPassword(User, hash ?? DummyHash, password);
        return hash is null
            ? (false, false)
            : (result != PasswordVerificationResult.Failed, result == PasswordVerificationResult.SuccessRehashNeeded);
    }

    public void EnsurePolicy(string field, string? password)
    {
        var min = settings.GetInt(SettingKeys.AuthPasswordMinLength);
        if (string.IsNullOrEmpty(password) || password.Length < min)
            throw new RequestValidationException(field, $"비밀번호는 {min}자 이상이어야 합니다.");
    }
}
