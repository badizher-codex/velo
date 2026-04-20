namespace VELO.Security.Models;

public record SafetyResult(
    SafetyLevel             Level,
    int                     NumericScore,       // -100 to +100
    IReadOnlyList<string>   ReasonsPositive,
    IReadOnlyList<string>   ReasonsNegative,
    string?                 ShortCircuitReason,
    DateTime                ComputedAt)
{
    public static SafetyResult Analyzing() => new(
        SafetyLevel.Analyzing,
        0,
        Array.Empty<string>(),
        Array.Empty<string>(),
        null,
        DateTime.UtcNow);

    public static SafetyResult Red(string shortCircuitReason) => new(
        SafetyLevel.Red,
        -100,
        Array.Empty<string>(),
        [shortCircuitReason],
        shortCircuitReason,
        DateTime.UtcNow);
}
