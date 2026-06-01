namespace Openza.Flow.Core.Models;

public enum DeviceFlowStatus
{
    RequestingCode,
    WaitingForUser,
    Polling,
    Success,
    Error,
    Expired,
    Cancelled
}

public sealed record DeviceCodeInfo(
    string DeviceCode,
    string UserCode,
    string VerificationUri,
    int ExpiresIn,
    int Interval);

public sealed record OAuthResult(
    string AccessToken,
    string TokenType,
    IReadOnlyList<string> Scopes);

public sealed record TokenValidationResult(
    bool IsValid,
    string? ErrorMessage,
    string? Username)
{
    public static TokenValidationResult Valid(string username) => new(true, null, username);

    public static TokenValidationResult Invalid(string errorMessage) => new(false, errorMessage, null);
}
