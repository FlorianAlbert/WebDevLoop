namespace WebDevLoop.Core.Orchestration.Results;

public enum CodingStandardsSeverity
{
    /// <summary>Breach of a documented standard, or a defect.</summary>
    Blocking,

    /// <summary>Clean-code smell judged worth fixing before integration.</summary>
    Judgement,
}
