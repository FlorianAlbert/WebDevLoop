namespace WebDevLoop.Core.Orchestration.Recovery.ExternalState;

/// <summary>Run event types recorded when reconciliation adopts a change that was made on GitHub outside the app.</summary>
public static class ExternalStateRunEvents
{
    public const string TicketClosedExternally = "TicketClosedExternally";

    public const string TicketRemovedFromSpec = "TicketRemovedFromSpec";

    public const string IntegratedTicketReopened = "IntegratedTicketReopened";

    public const string PullRequestBaseRetargeted = "PullRequestBaseRetargeted";
}
