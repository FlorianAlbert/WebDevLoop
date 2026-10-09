using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.SpecQueue;
using WebDevLoop.Core.Queries;

namespace WebDevLoop.Web.Components.Dashboard;

public static class SpecQueueEvents
{
    /// <summary>Events that change what the dashboard and queue show: specs entering the queue or changing status.</summary>
    public static bool Affects(LiveEventView liveEvent) =>
        liveEvent.Type is nameof(SpecRunStatusChanged) or nameof(SpecRunQueued);
}
