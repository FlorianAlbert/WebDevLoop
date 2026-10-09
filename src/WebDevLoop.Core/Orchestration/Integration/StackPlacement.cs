using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.Integration;

/// <summary>Where a ticket's layer sits in the PR stack.</summary>
/// <param name="Own">The ticket's persisted layer, once its PR exists.</param>
/// <param name="Below">The layer directly below: the previous layer of the spec or, for the first layer of a
/// <see cref="SpecDependencyMode.StackOnTop"/> spec, the blocking spec's top layer. Null for a bottom PR on trunk.</param>
/// <param name="SpecLayers">The spec's layers bottom to top (including <paramref name="Own"/>).</param>
internal sealed record StackPlacement(
    PullStackLayer? Own,
    PullStackLayer? Below,
    BranchName BaseBranch,
    int Position,
    IReadOnlyList<PullStackLayer> SpecLayers);
