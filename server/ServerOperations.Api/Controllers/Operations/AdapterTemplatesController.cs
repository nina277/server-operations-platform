using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ServerOperations.Core.Adapters.Interfaces;
using ServerOperations.Api.DTOs.Common;
using ServerOperations.Api.DTOs.Operations;
using ServerOperations.Api.Middleware;

namespace ServerOperations.Api.Controllers.Operations;

[ApiController]
[Route("api/v1/adapter-templates")]
[Authorize]
public class AdapterTemplatesController(IAdapterTemplateCatalog catalog) : ControllerBase
{
    [HttpGet]
    public ActionResult<ApiResponse<List<AdapterTemplateDto>>> GetAll()
    {
        var templates = catalog.GetAll().Select(ToDto).ToList();
        return Ok(ApiResponse<List<AdapterTemplateDto>>.Ok(templates, TraceId()));
    }

    [HttpGet("{id}")]
    public ActionResult<ApiResponse<AdapterTemplateDto>> Get(string id)
    {
        var template = catalog.Find(id)
            ?? throw AppException.NotFound("template_not_found", "テンプレートが見つかりません。");
        return Ok(ApiResponse<AdapterTemplateDto>.Ok(ToDto(template), TraceId()));
    }

    private static AdapterTemplateDto ToDto(AdapterTemplate template) => new(
        template.Id,
        template.Name,
        template.Description,
        template.Inputs.Select(i => new AdapterTemplateInputDto(
            i.Key, i.Label, i.Type.ToString().ToLowerInvariant(), i.Required, i.Secret,
            i.Description, i.DefaultValue)).ToList(),
        template.RecommendedMonitors,
        template.InitialRules,
        template.AllowedOperations,
        template.Capabilities);

    private string TraceId() => ExceptionHandlingMiddleware.GetTraceId(HttpContext);
}
