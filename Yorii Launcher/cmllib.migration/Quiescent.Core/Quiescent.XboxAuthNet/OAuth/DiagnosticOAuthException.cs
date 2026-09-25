namespace Quiescent.XboxAuthNet.OAuth;

/// <summary>
/// Diagnostic exception carrying the failing OAuth request's query so the
/// caller can log exactly what login.live.com rejected.
/// </summary>
public class DiagnosticOAuthException : Exception
{
    public int StatusCode { get; }
    public string? RequestQuery { get; }

    public DiagnosticOAuthException(string message, int statusCode, string? requestQuery)
        : base(message)
    {
        StatusCode = statusCode;
        RequestQuery = requestQuery;
    }
}
