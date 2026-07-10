using System.Net;

namespace ServerOperations.Api.Services.Interfaces;

/// <summary>現在のリクエストの操作者情報へのアクセサ。</summary>
public interface ICurrentUserAccessor
{
    long? UserId { get; }

    string? Username { get; }

    IPAddress? RemoteIp { get; }
}
