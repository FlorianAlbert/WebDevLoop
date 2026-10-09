namespace WebDevLoop.Infrastructure.Prerequisites;

public enum GhStackMode
{
    /// <summary>Stacks are linked through the REST API; <c>gh stack</c> is only an optional fallback.</summary>
    RestWithOptionalFallback,

    /// <summary>The stack REST API is unavailable, so <c>gh stack</c> must be installed.</summary>
    FallbackRequired,
}
