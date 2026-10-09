namespace WebDevLoop.Core.Domain;

/// <summary><paramref name="Blocked"/> cannot start until <paramref name="Blocking"/> is done.</summary>
public readonly record struct DependencyEdge<T>(T Blocked, T Blocking)
    where T : notnull;
