namespace ServerOperations.Api.DTOs.Common;

public record ApiError(string Code, string Message);

/// <summary>全APIで統一するレスポンス形式。</summary>
public record ApiResponse<T>
{
    public bool Success { get; init; }

    public T? Data { get; init; }

    public ApiError? Error { get; init; }

    public string? TraceId { get; init; }

    public static ApiResponse<T> Ok(T data, string? traceId = null) =>
        new() { Success = true, Data = data, TraceId = traceId };

    public static ApiResponse<T> Fail(string code, string message, string? traceId = null) =>
        new() { Success = false, Error = new ApiError(code, message), TraceId = traceId };
}
