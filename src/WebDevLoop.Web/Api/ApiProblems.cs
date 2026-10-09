using Microsoft.AspNetCore.Http.HttpResults;
using WebDevLoop.Core.Management;
using WebDevLoop.Core.Orchestration.Control;
using WebDevLoop.Core.Settings;
using WebDevLoop.Infrastructure.Prerequisites;

namespace WebDevLoop.Web.Api;

/// <summary>RFC 9457 problem responses. Every error carries a body so the host's status-code pages never replace it with HTML.</summary>
internal static class ApiProblems
{
    public static ProblemHttpResult NotFound(string detail) => TypedResults.Problem(detail, statusCode: StatusCodes.Status404NotFound, title: "Not found");

    public static ProblemHttpResult Conflict(string detail) => TypedResults.Problem(detail, statusCode: StatusCodes.Status409Conflict, title: "Conflict");

    public static ProblemHttpResult BadRequest(string detail) => TypedResults.Problem(detail, statusCode: StatusCodes.Status400BadRequest, title: "Bad request");

    public static ValidationProblem Validation(IEnumerable<SettingsValidationError> errors) => TypedResults.ValidationProblem(
        errors.GroupBy(error => error.Field).ToDictionary(group => group.Key, group => group.Select(error => error.Message).ToArray()));

    public static ProblemHttpResult PrerequisitesFailed(ReadinessSnapshot snapshot) => TypedResults.Problem(
        snapshot.Mode == ReadinessMode.DiagnosticOnly && snapshot.Report is null
            ? "Prerequisites have not been evaluated yet; workflow actions are disabled."
            : "Prerequisites failed; workflow actions are disabled until they pass.",
        statusCode: StatusCodes.Status503ServiceUnavailable,
        title: "Prerequisites not satisfied",
        extensions: new Dictionary<string, object?>
        {
            ["mode"] = snapshot.Mode.ToString(),
            ["failedChecks"] = snapshot.FailedChecks.Select(check => check.ToResponse()).ToArray(),
        });

    /// <summary>Maps a refused control command to its problem response; <paramref name="result"/> must not be applied.</summary>
    public static ProblemHttpResult ForControl(ControlResult result) => result.Outcome switch
    {
        ControlOutcome.NotFound => NotFound(result.Reason ?? "Not found."),
        ControlOutcome.NotAllowed => TypedResults.Problem(result.Reason, statusCode: StatusCodes.Status409Conflict, title: "Not allowed in the run's current state"),
        ControlOutcome.NoActiveSlot => TypedResults.Problem(result.Reason, statusCode: StatusCodes.Status409Conflict, title: "No free active-spec slot"),
        ControlOutcome.ConcurrencyConflict => Conflict(result.Reason ?? "Conflict."),
        _ => throw new ArgumentException("An applied command is not a problem.", nameof(result)),
    };

    /// <summary>Maps a failed command to its problem response; <paramref name="result"/> must not be a success.</summary>
    public static IResult ForFailure<T>(CommandResult<T> result) => result.Status switch
    {
        CommandStatus.NotFound => NotFound(result.Message ?? "Not found."),
        CommandStatus.Invalid => Validation(result.Errors ?? []),
        CommandStatus.Conflict => Conflict(result.Message ?? "Conflict."),
        _ => throw new ArgumentException("A successful result is not a problem.", nameof(result)),
    };
}
