using ServerOperations.Api.DTOs.Operations;

namespace ServerOperations.Api.Services.Interfaces;

public interface IOperationsInsightsService
{
    Task<OperationsInsightsDto> GetAsync(DateTime from, DateTime to, CancellationToken ct = default);
}

public interface IIncidentNoteService
{
    Task<List<IncidentNoteDto>> GetForIncidentAsync(long incidentId, CancellationToken ct = default);

    Task<IncidentNoteDto> AddAsync(
        long incidentId, CreateIncidentNoteRequest request, CancellationToken ct = default);
}

public interface IMaintenanceWindowService
{
    Task<List<MaintenanceWindowDto>> GetUpcomingAsync(CancellationToken ct = default);

    Task<MaintenanceWindowDto> CreateAsync(
        CreateMaintenanceWindowRequest request, CancellationToken ct = default);

    Task<MaintenanceWindowDto> CancelAsync(long id, CancellationToken ct = default);
}
