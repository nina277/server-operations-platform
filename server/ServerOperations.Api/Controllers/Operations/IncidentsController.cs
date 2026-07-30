using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ServerOperations.Api.DTOs.Common;
using ServerOperations.Api.DTOs.Operations;
using ServerOperations.Api.Extensions;
using ServerOperations.Api.Middleware;
using ServerOperations.Api.Services.Interfaces;
using ServerOperations.Core.Repositories.Interfaces;

namespace ServerOperations.Api.Controllers.Operations;

[ApiController]
[Route("api/v1/incidents")]
[Authorize]
public class IncidentsController(
    IIncidentService incidentService,
    IDiagnosisRepository diagnoses,
    ServerOperations.Core.Services.Ai.IAiDiagnosisGateway aiGateway,
    IIncidentLogRepository incidentLogs,
    IIncidentRepository incidentRepository,
    IIncidentNoteService notes,
    IOperationsInsightsRepository insights) : ControllerBase
{
    /// <summary>
    /// この障害が過去に何回起きたか、前回は何をして直ったか。
    /// 同じ障害の再発時に、前回の対応をたどれるようにする。
    /// </summary>
    [HttpGet("{id:long}/recurrence")]
    public async Task<ActionResult<ApiResponse<RecurrenceDto>>> GetRecurrence(
        long id, CancellationToken ct)
    {
        var incident = await incidentRepository.FindByIdAsync(id, ct)
            ?? throw AppException.NotFound("incident_not_found", "インシデントが見つかりません。");

        var summary = await insights.GetRecurrenceAsync(
            incident.TargetId, incident.SignatureSha256, incident.Id, ct);

        return Ok(ApiResponse<RecurrenceDto>.Ok(new RecurrenceDto
        {
            TotalCount = summary.TotalCount,
            ResolvedCount = summary.ResolvedCount,
            FirstOccurredAt = summary.FirstOccurredAt,
            PreviousOccurredAt = summary.PreviousOccurredAt,
            LastSuccessfulActionId = summary.LastSuccessfulActionId,
            LastSuccessfulAt = summary.LastSuccessfulAt,
        }, TraceId()));
    }

    [HttpGet("{id:long}/notes")]
    public async Task<ActionResult<ApiResponse<List<IncidentNoteDto>>>> GetNotes(
        long id, CancellationToken ct)
    {
        var items = await notes.GetForIncidentAsync(id, ct);
        return Ok(ApiResponse<List<IncidentNoteDto>>.Ok(items, TraceId()));
    }

    /// <summary>
    /// 対応メモを追加する。運用の判断を書き残す操作のため、
    /// 閲覧のみの利用者には開かない。書き換えと削除の口は用意しない。
    /// </summary>
    [HttpPost("{id:long}/notes")]
    [Authorize(Policy = AuthorizationPolicies.AdminWithRecentMfa)]
    public async Task<ActionResult<ApiResponse<IncidentNoteDto>>> AddNote(
        long id, [FromBody] CreateIncidentNoteRequest request, CancellationToken ct)
    {
        var note = await notes.AddAsync(id, request, ct);
        return Ok(ApiResponse<IncidentNoteDto>.Ok(note, TraceId()));
    }

    /// <summary>
    /// AIによる再診断を要求する。AI無効・上限到達・失敗時は診断を作らず理由を返す(復旧は開始しない)。
    /// </summary>
    [HttpPost("{id:long}/rediagnose")]
    [Authorize(Policy = AuthorizationPolicies.AdminWithRecentMfa)]
    public async Task<ActionResult<ApiResponse<RediagnoseResultDto>>> Rediagnose(
        long id, CancellationToken ct)
    {
        var dto = await incidentService.GetAsync(id, ct);
        var incident = await incidentRepository.FindByIdAsync(id, ct)
            ?? throw AppException.NotFound("incident_not_found", "インシデントが見つかりません。");

        // 直近のマスク済みログ抜粋を入力にする(AI送信前にさらに匿名化・縮小される)
        var logs = await incidentLogs.GetRecentAsync(dto.TargetId, 5, ct);
        var logExcerpt = string.Join('\n', logs.Select(l => l.MaskedContent));

        var result = await aiGateway.DiagnoseAsync(incident, logExcerpt, ct);

        return Ok(ApiResponse<RediagnoseResultDto>.Ok(new RediagnoseResultDto
        {
            Diagnosis = result.Diagnosis is null ? null : DiagnosisDto.From(result.Diagnosis),
            Outcome = result.Diagnosis is not null ? "Diagnosed" : result.SkipReason.ToString(),
            Message = result.Message,
        }, TraceId()));
    }

    [HttpGet("{id:long}/diagnoses")]
    public async Task<ActionResult<ApiResponse<List<DiagnosisDto>>>> GetDiagnoses(
        long id, CancellationToken ct)
    {
        // インシデントの存在確認を兼ねる(見つからなければ404)
        await incidentService.GetAsync(id, ct);
        var items = await diagnoses.GetByIncidentAsync(id, ct);
        return Ok(ApiResponse<List<DiagnosisDto>>.Ok(
            items.Select(DiagnosisDto.From).ToList(), TraceId()));
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<IncidentDto>>>> Search(
        [FromQuery] IncidentListQuery query, CancellationToken ct)
    {
        var result = await incidentService.SearchAsync(query, ct);
        return Ok(ApiResponse<PagedResult<IncidentDto>>.Ok(result, TraceId()));
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<ApiResponse<IncidentDto>>> Get(long id, CancellationToken ct)
    {
        var result = await incidentService.GetAsync(id, ct);
        return Ok(ApiResponse<IncidentDto>.Ok(result, TraceId()));
    }

    [HttpPatch("{id:long}/status")]
    [Authorize(Policy = AuthorizationPolicies.AdminWithRecentMfa)]
    public async Task<ActionResult<ApiResponse<IncidentDto>>> UpdateStatus(
        long id, [FromBody] UpdateIncidentStatusRequest request, CancellationToken ct)
    {
        var result = await incidentService.UpdateStatusAsync(id, request.Status, ct);
        return Ok(ApiResponse<IncidentDto>.Ok(result, TraceId()));
    }

    private string TraceId() => ExceptionHandlingMiddleware.GetTraceId(HttpContext);
}
