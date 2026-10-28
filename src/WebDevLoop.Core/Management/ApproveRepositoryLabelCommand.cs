namespace WebDevLoop.Core.Management;

/// <summary>Explicit permission to create the fixed repository label. False never creates it.</summary>
public sealed record ApproveRepositoryLabelCommand(bool ApproveLabelCreation = false);
