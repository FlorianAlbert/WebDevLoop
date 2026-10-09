using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Ports;

/// <summary>A reserved port for one tester run; <paramref name="Environment"/> is passed to the tester's shell.</summary>
public sealed record TestTarget(RunId SpecRunId, int Port, Uri AppUrl, IReadOnlyDictionary<string, string> Environment);
