using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ServerOperations.Api.DTOs.Common;
using ServerOperations.Api.DTOs.Operations;
using ServerOperations.Api.Middleware;
using ServerOperations.Core.Services;

namespace ServerOperations.Api.Controllers.Operations;

/// <summary>
/// 復旧アクションの許可リスト。
/// 危険度や承認要否を画面側で作り直さず、サーバー側の定義をそのまま参照させるために公開する。
/// High操作はカタログに存在しないため、ここにも現れない。
/// </summary>
[ApiController]
[Route("api/v1/recovery-action-catalog")]
[Authorize]
public class RecoveryActionCatalogController(IRecoveryActionCatalog catalog) : ControllerBase
{
    [HttpGet]
    public ActionResult<ApiResponse<List<RecoveryActionDefinitionDto>>> GetAll()
    {
        var items = catalog.GetAll().Select(RecoveryActionDefinitionDto.From).ToList();
        return Ok(ApiResponse<List<RecoveryActionDefinitionDto>>.Ok(items, TraceId()));
    }

    private string TraceId() => ExceptionHandlingMiddleware.GetTraceId(HttpContext);
}
