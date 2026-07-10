namespace ServerOperations.Api.Services;

/// <summary>
/// 業務エラー。メッセージは利用者へそのまま返せる内容に限定する(内部情報・秘密情報を含めない)。
/// </summary>
public class AppException(int statusCode, string code, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;

    public string Code { get; } = code;

    public static AppException BadRequest(string code, string message) =>
        new(StatusCodes.Status400BadRequest, code, message);

    public static AppException Unauthorized(string code, string message) =>
        new(StatusCodes.Status401Unauthorized, code, message);

    public static AppException Forbidden(string code, string message) =>
        new(StatusCodes.Status403Forbidden, code, message);

    public static AppException NotFound(string code, string message) =>
        new(StatusCodes.Status404NotFound, code, message);

    public static AppException Conflict(string code, string message) =>
        new(StatusCodes.Status409Conflict, code, message);
}
