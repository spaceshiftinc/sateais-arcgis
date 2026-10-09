namespace SateAIs.Core.Api;

public class SateAIsException : Exception
{
    public SateAIsException(string message) : base(message) { }

    public SateAIsException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>The API answered with an error, or could not be reached (status 0).</summary>
public class ApiException : SateAIsException
{
    public ApiException(int statusCode, string? code, string serverMessage, Exception? inner = null)
        : base(Describe(statusCode, code, serverMessage), inner!)
    {
        StatusCode = statusCode;
        Code = code;
        ServerMessage = serverMessage;
    }

    public int StatusCode { get; }

    /// <summary>The API's own error code, when it sent one (e.g. <c>SCENE_NOT_FOUND</c>).</summary>
    public string? Code { get; }

    /// <summary>The server's text. Diagnostics only; never shown to the user as is.</summary>
    public string ServerMessage { get; }

    public static ApiException ForStatus(int statusCode, string? code, string message) => statusCode switch
    {
        400 => new ValidationException(code, message),
        401 => new AuthenticationException(code, message),
        402 => new InsufficientCreditsException(code, message),
        403 => new PermissionDeniedException(code, message),
        404 or 410 => new NotFoundException(statusCode, code, message),
        409 => new ConflictException(code, message),
        413 => new PayloadTooLargeException(code, message),
        429 => new RateLimitException(code, message),
        500 or 502 or 503 or 504 => new ServerException(statusCode, code, message),
        _ => new ApiException(statusCode, code, message),
    };

    private static string Describe(int statusCode, string? code, string message) =>
        code is null ? $"HTTP {statusCode}: {message}" : $"HTTP {statusCode} [{code}]: {message}";
}

public sealed class ValidationException(string? code, string message) : ApiException(400, code, message);

public sealed class AuthenticationException(string? code, string message) : ApiException(401, code, message);

public sealed class InsufficientCreditsException(string? code, string message) : ApiException(402, code, message);

public sealed class PermissionDeniedException(string? code, string message) : ApiException(403, code, message);

/// <summary>404 or 410. The reason is in <see cref="ApiException.Code"/> (e.g. <c>GONE</c>).</summary>
public sealed class NotFoundException(int statusCode, string? code, string message) : ApiException(statusCode, code, message);

public sealed class ConflictException(string? code, string message) : ApiException(409, code, message);

public sealed class PayloadTooLargeException(string? code, string message) : ApiException(413, code, message);

public sealed class RateLimitException(string? code, string message) : ApiException(429, code, message);

public sealed class ServerException(int statusCode, string? code, string message) : ApiException(statusCode, code, message);

/// <summary>The job succeeded, but its result is not GeoJSON and cannot become a layer.</summary>
public sealed class UnsupportedResultFormatException(string contentType)
    : SateAIsException($"result is {(string.IsNullOrEmpty(contentType) ? "not JSON" : contentType)}, not GeoJSON")
{
    public string ContentType { get; } = contentType;
}

public sealed class InvalidAnalysisRequestException(string message) : SateAIsException(message);
