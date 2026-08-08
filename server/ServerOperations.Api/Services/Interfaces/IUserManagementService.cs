using ServerOperations.Api.DTOs.Auth;

namespace ServerOperations.Api.Services.Interfaces;

public interface IUserManagementService
{
    Task<List<ManagedUserDto>> GetAllAsync(CancellationToken ct = default);

    Task<ManagedUserDto> CreateAsync(CreateUserRequest request, CancellationToken ct = default);

    Task<ManagedUserDto> UpdateRoleAsync(
        long userId, UpdateUserRoleRequest request, CancellationToken ct = default);

    /// <summary>有効・無効を切り替える。削除はしない。</summary>
    Task<ManagedUserDto> UpdateActiveAsync(
        long userId, UpdateUserActiveRequest request, CancellationToken ct = default);

    /// <summary>
    /// 他人のMFAを解除する。端末を失ったときの回復手段。
    /// 対象の全セッションを失効させ、必ず監査に残す。
    /// </summary>
    Task<ManagedUserDto> ResetMfaAsync(long userId, CancellationToken ct = default);
}
