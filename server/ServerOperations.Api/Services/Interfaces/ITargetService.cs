using ServerOperations.Api.DTOs.Operations;

namespace ServerOperations.Api.Services.Interfaces;

public interface ITargetService
{
    Task<List<TargetDto>> GetAllAsync(CancellationToken ct = default);

    Task<TargetDto> GetAsync(long id, CancellationToken ct = default);

    Task<TargetDto> CreateAsync(CreateTargetRequest request, CancellationToken ct = default);

    Task<TargetDto> UpdateAsync(long id, UpdateTargetRequest request, CancellationToken ct = default);

    Task<TargetCapabilitiesDto> GetCapabilitiesAsync(long id, CancellationToken ct = default);

    /// <summary>登録済み対象への接続試験。任意URLは受け取らない。</summary>
    Task<ConnectionTestResultDto> TestConnectionAsync(long id, CancellationToken ct = default);
}
