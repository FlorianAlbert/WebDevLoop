using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebDevLoop.Core.Domain;

namespace WebDevLoop.Infrastructure.Persistence.Configurations;

internal sealed class SettingsProfileConfiguration : IEntityTypeConfiguration<SettingsProfile>
{
    private const string RolesField = "_roles";
    private const string ScopeKey = "ScopeKey";

    public void Configure(EntityTypeBuilder<SettingsProfile> builder)
    {
        builder.ToTable("SettingsProfiles");
        builder.HasKey(profile => profile.Id);
        builder.ComplexProperty<TestPortRange>(profile => profile.TestPortRange, range =>
        {
            range.Property(value => value.Start);
            range.Property(value => value.End);
        });
        builder.HasOne<RepositoryRecord>().WithMany().HasForeignKey(profile => profile.RepositoryId).OnDelete(DeleteBehavior.Cascade);

        // 0 for the global row, the repository id otherwise: one unique index enforces a single global profile and one override per repository.
        builder.Property<int>(ScopeKey).HasComputedColumnSql("COALESCE(\"RepositoryId\", 0)", stored: true);
        builder.HasIndex(ScopeKey).IsUnique().HasDatabaseName("UX_SettingsProfiles_Scope");

        builder.Property<Dictionary<AgentRole, RoleSettingsOverride>>(RolesField)
            .HasColumnName("RolesJson")
            .HasConversion(
                roles => JsonSerializer.Serialize(roles, RoleJson.Options),
                json => JsonSerializer.Deserialize<Dictionary<AgentRole, RoleSettingsOverride>>(json, RoleJson.Options)!,
                new ValueComparer<Dictionary<AgentRole, RoleSettingsOverride>>(
                    (left, right) => RoleJson.Serialize(left) == RoleJson.Serialize(right),
                    roles => RoleJson.Serialize(roles).GetHashCode(),
                    roles => RoleJson.Clone(roles)))
            .IsRequired();
    }

    private static class RoleJson
    {
        public static readonly JsonSerializerOptions Options = new() { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

        public static string Serialize(Dictionary<AgentRole, RoleSettingsOverride>? roles) =>
            JsonSerializer.Serialize(roles?.OrderBy(pair => pair.Key).ToDictionary(), Options);

        public static Dictionary<AgentRole, RoleSettingsOverride> Clone(Dictionary<AgentRole, RoleSettingsOverride> roles) => new(roles);
    }
}
