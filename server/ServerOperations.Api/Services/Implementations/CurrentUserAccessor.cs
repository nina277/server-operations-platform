using System.Net;
using System.Security.Claims;
using ServerOperations.Api.Services.Interfaces;

namespace ServerOperations.Api.Services.Implementations;

public class CurrentUserAccessor(IHttpContextAccessor httpContextAccessor) : ICurrentUserAccessor
{
    public long? UserId
    {
        get
        {
            var value = httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
            return long.TryParse(value, out var id) ? id : null;
        }
    }

    public string? Username => httpContextAccessor.HttpContext?.User.Identity?.Name;

    public IPAddress? RemoteIp => httpContextAccessor.HttpContext?.Connection.RemoteIpAddress;
}
