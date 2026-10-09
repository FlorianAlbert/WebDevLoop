using Microsoft.EntityFrameworkCore;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.Persistence.Repositories;

public sealed class EfSettingsProfileRepository(WebDevLoopDbContext context) : ISettingsProfileRepository
{
    public async Task<SettingsProfile?> GetGlobalAsync(CancellationToken cancellationToken) =>
        await context.SettingsProfiles.FirstOrDefaultAsync(profile => profile.RepositoryId == null, cancellationToken);

    public async Task<SettingsProfile?> FindForRepositoryAsync(int repositoryId, CancellationToken cancellationToken) =>
        await context.SettingsProfiles.FirstOrDefaultAsync(profile => profile.RepositoryId == repositoryId, cancellationToken);

    public void Add(SettingsProfile profile) => context.SettingsProfiles.Add(profile);
}
