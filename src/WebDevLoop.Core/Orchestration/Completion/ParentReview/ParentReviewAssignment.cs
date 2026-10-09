using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.Completion.ParentReview;

/// <summary>A spec in <see cref="SpecRunStatus.ParentReviewing"/> whose final parent-spec review must run.</summary>
public sealed record ParentReviewAssignment(RunId SpecRunId);
