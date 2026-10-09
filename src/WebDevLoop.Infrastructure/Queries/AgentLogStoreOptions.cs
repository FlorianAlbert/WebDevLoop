namespace WebDevLoop.Infrastructure.Queries;

public sealed record AgentLogStoreOptions(int MaxEntriesPerStep = 5000, int MaxSteps = 200);
