using WebDevLoop.Core.Management;
using WebDevLoop.Core.Queries;

namespace WebDevLoop.Web.Components.Dashboard;

public sealed record RepositoryOverview(RepositoryView Repository, IReadOnlyList<SpecRunView> Runs, EffectiveSettingsView? Settings);
