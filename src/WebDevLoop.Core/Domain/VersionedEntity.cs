namespace WebDevLoop.Core.Domain;

/// <summary>
/// Base for mutable aggregates. <see cref="Version"/> is the compare-and-swap token: persistence updates a row
/// only when its stored version equals the version that was loaded, then calls <see cref="AdvanceVersion"/>.
/// </summary>
public abstract class VersionedEntity
{
    public int Version { get; private set; }

    public void AdvanceVersion() => Version++;
}
