namespace WebDevLoop.Core.Ports;

/// <summary>Diagnoses external prerequisites; reports failures as data instead of throwing.</summary>
public interface IPrerequisiteValidator
{
    Task<PrerequisiteReport> ValidateAsync(CancellationToken cancellationToken);
}
