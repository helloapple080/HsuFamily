using FamilyPortal.Application.Common;
using FamilyPortal.Application.DTOs.Member;

namespace FamilyPortal.Application.Interfaces;

/// <summary>家庭成員 use case 服務。</summary>
public interface IMemberService
{
    /// <summary>取得數量受限的啟用成員。</summary>
    Task<ServiceResult<IReadOnlyList<MemberResponse>>> GetActiveAsync(
        CancellationToken cancellationToken);

    /// <summary>依識別碼取得家庭成員。</summary>
    Task<ServiceResult<MemberResponse>> GetByIdAsync(
        string id,
        CancellationToken cancellationToken);

    /// <summary>建立家庭成員。</summary>
    Task<ServiceResult<MemberResponse>> CreateAsync(
        UpsertMemberRequest request,
        CancellationToken cancellationToken);

    /// <summary>更新家庭成員。</summary>
    Task<ServiceResult<MemberResponse>> UpdateAsync(
        string id,
        UpsertMemberRequest request,
        CancellationToken cancellationToken);

    /// <summary>刪除家庭成員。</summary>
    Task<ServiceResult<bool>> DeleteAsync(
        string id,
        CancellationToken cancellationToken);
}
