using FamilyPortal.Application.Interfaces;
using FamilyPortal.Domain.Entities;
using FamilyPortal.Domain.Enums;

/// <summary>預設管理員初始化結果。</summary>
public enum DefaultAdminBootstrapResult
{
    /// <summary>已建立新的預設管理員。</summary>
    Created,

    /// <summary>系統已存在管理員。</summary>
    Existing
}

/// <summary>協調 duplicate-safe 的預設管理員初始化。</summary>
public static class DefaultAdminBootstrap
{
    /// <summary>確保系統至少有一位管理員，並辨識唯一識別值與非管理員的衝突。</summary>
    /// <param name="repository">使用者 repository。</param>
    /// <param name="candidateFactory">僅在需要 insert 時建立預設管理員。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>建立新管理員或已存在管理員的結果。</returns>
    /// <exception cref="ArgumentNullException">Repository 或 candidate factory 未提供。</exception>
    /// <exception cref="InvalidOperationException">Factory 回傳 null，或預設管理員識別值與既有非管理員帳號衝突。</exception>
    public static async Task<DefaultAdminBootstrapResult> EnsureAsync(
        IUserRepository repository,
        Func<User> candidateFactory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(candidateFactory);

        if (await AdminExistsAsync(repository, cancellationToken))
        {
            return DefaultAdminBootstrapResult.Existing;
        }

        var candidate = candidateFactory()
            ?? throw new InvalidOperationException("預設管理員 factory 不可回傳 null。");
        if (await repository.TryCreateAsync(candidate, cancellationToken))
        {
            return DefaultAdminBootstrapResult.Created;
        }

        if (await AdminExistsAsync(repository, cancellationToken))
        {
            return DefaultAdminBootstrapResult.Existing;
        }

        throw new InvalidOperationException(
            "預設管理員帳號或電子信箱與現有非管理員帳號衝突，請修正 DefaultAdmin 設定。");
    }

    private static Task<bool> AdminExistsAsync(
        IUserRepository repository,
        CancellationToken cancellationToken) =>
        repository.ExistsAsync(user => user.Role == UserRole.Admin, cancellationToken);
}
