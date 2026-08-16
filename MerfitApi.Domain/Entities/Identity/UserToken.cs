using Merfit.Domain.Common;

namespace Merfit.Domain.Entities.Identity;

public enum UserTokenPurpose
{
    EmailVerification,
    PasswordReset
}

/// <summary>Single-use, hashed tokens backing email verification and password reset flows.</summary>
public sealed class UserToken : BaseEntity
{
    public Guid UserId { get; set; }
    public required string TokenHash { get; set; }
    public UserTokenPurpose Purpose { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UsedAt { get; set; }

    public bool IsValid => UsedAt is null && DateTimeOffset.UtcNow < ExpiresAt;
}
