using System.Globalization;
using SateAIs.Core.Api;
using SateAIs.Core.Credentials;

namespace SateAIs.AddIn.Analysis;

/// <summary>What the user reads when a request fails. Same sentences as the QGIS plugin; never the server's text.</summary>
internal static class Sentences
{
    public const string InvalidInput = "Please fill in the required fields.";
    public const string ServerUnavailable = "The server is temporarily unavailable. Please try again later.";
    public const string NoNetwork = "No network connection. Please check your internet.";

    public static string ForSubmit(Exception e) => e switch
    {
        AuthNotConfiguredException => "Please register an API key first.",
        InvalidAnalysisRequestException => InvalidInput,
        AuthenticationException => "Invalid API key. Please check your settings.",
        PermissionDeniedException => "This analysis is not enabled for your account. Please request access from the console.",
        InsufficientCreditsException => "Insufficient credits. Please top up in the console.",
        ValidationException => "This request could not be run as written. Check the area and the dates.",
        NotFoundException => "The requested data was not found (no matching scene or polygon). Please adjust the area or date and try again.",
        ConflictException => "The resource is not ready yet. Please wait a moment and try again.",
        PayloadTooLargeException => "The request was too large for the server.",
        RateLimitException => "You already have the maximum number of jobs running. Wait for one to finish, then submit again.",
        ApiException => ServerUnavailable,
        _ => NoNetwork,
    };

    /// <summary>The estimate can say how far over the area limit the request is; otherwise as a submission.</summary>
    public static string ForEstimate(Exception e)
    {
        if (e is ValidationException validation)
        {
            var reason = Core.Wording.Wording.AreaLimitReason(validation.ServerMessage);
            if (reason.Length > 0)
            {
                return reason;
            }
        }
        return ForSubmit(e);
    }

    public static string ForConnectionTest(Exception e) => e switch
    {
        AuthenticationException => "Invalid API key. Please check the key issued in the console.",
        PermissionDeniedException => "Your account does not have access to this endpoint. Please contact support.",
        NotFoundException => "Could not reach the server. Please try again shortly.",
        ApiException => "The server is not responding. Please try again in a moment.",
        _ => "No network connection. Please check your internet connection.",
    };

    public static string Connected(Account account)
    {
        if (account.Email is { Length: > 0 } email)
        {
            return account.CreditBalance is { } balance
                ? $"Connected as {email} ({balance.ToString("N0", CultureInfo.InvariantCulture)} credits remaining)."
                : $"Connected as {email}.";
        }
        return "Connected.";
    }
}
