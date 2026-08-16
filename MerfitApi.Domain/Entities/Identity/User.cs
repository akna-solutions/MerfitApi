using Merfit.Domain.Common;
using Merfit.Domain.Enums;

namespace Merfit.Domain.Entities.Identity;

public sealed class User : AuditableEntity, ISoftDeletable
{
    public required string Email { get; set; }
    public required string NormalizedEmail { get; set; }
    public required string PasswordHash { get; set; }
    public required string Username { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public Gender? Gender { get; set; }
    public DateOnly? BirthDate { get; set; }
    public string? AvatarUrl { get; set; }
    public UnitSystem UnitSystem { get; set; } = UnitSystem.Metric;
    public string PreferredLanguage { get; set; } = "en";
    public bool IsEmailVerified { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(FirstName)
        ? Username
        : $"{FirstName} {LastName}".Trim();
}
