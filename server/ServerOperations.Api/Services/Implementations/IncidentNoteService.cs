using ServerOperations.Api.DTOs.Operations;
using ServerOperations.Api.Services.Interfaces;
using ServerOperations.Core.Models.Auth;
using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Repositories.Interfaces;

namespace ServerOperations.Api.Services.Implementations;

/// <summary>
/// インシデントへの対応メモ。人が書いた記録を残す。
/// 書き換えと削除の口は用意しない。対応の記録が後から書き換わると、
/// 何が起きて何をしたのかの手掛かりとして信用できなくなるため。
/// </summary>
public class IncidentNoteService(
    IIncidentNoteRepository notes,
    IIncidentRepository incidents,
    IAuditService audit,
    ICurrentUserAccessor currentUser,
    TimeProvider timeProvider) : IIncidentNoteService
{
    public async Task<List<IncidentNoteDto>> GetForIncidentAsync(
        long incidentId, CancellationToken ct = default)
    {
        await RequireIncidentAsync(incidentId, ct);

        var items = await notes.GetForIncidentAsync(incidentId, ct);
        return items.Select(ToDto).ToList();
    }

    public async Task<IncidentNoteDto> AddAsync(
        long incidentId, CreateIncidentNoteRequest request, CancellationToken ct = default)
    {
        await RequireIncidentAsync(incidentId, ct);

        var body = request.Body.Trim();
        if (body.Length == 0)
        {
            throw AppException.BadRequest("note_body_required", "メモの内容を入力してください。");
        }

        var note = new IncidentNote
        {
            IncidentId = incidentId,
            AuthorUserId = currentUser.UserId,
            AuthorName = currentUser.Username ?? "unknown",
            Body = body,
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
        };

        await notes.AddAsync(note, ct);
        await notes.SaveChangesAsync(ct);

        // 本文はそのまま監査に載せない。長さだけを残し、内容は本体で読む。
        await audit.RecordAsync(
            "incident.note.add", "Incident", incidentId.ToString(), AuditResult.Success,
            details: $"noteId={note.Id} length={body.Length}", ct: ct);

        return ToDto(note);
    }

    private async Task RequireIncidentAsync(long incidentId, CancellationToken ct)
    {
        var incident = await incidents.FindByIdAsync(incidentId, ct);
        if (incident is null)
        {
            throw AppException.NotFound("incident_not_found", "インシデントが見つかりません。");
        }
    }

    private static IncidentNoteDto ToDto(IncidentNote note) => new()
    {
        Id = note.Id,
        AuthorName = note.AuthorName,
        Body = note.Body,
        CreatedAt = note.CreatedAt,
    };
}
