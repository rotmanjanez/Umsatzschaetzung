namespace Umsatzschaetzung.Service;

public enum ErrorCode { Invalid, NotFound, Conflict, Unavailable, Unsupported, Internal }

public sealed class ServiceError(ErrorCode code, string message, object? details = null, Exception? inner = null)
    : Exception(message, inner)
{
    public ErrorCode Code { get; } = code;
    public object? Details { get; } = details;
}
