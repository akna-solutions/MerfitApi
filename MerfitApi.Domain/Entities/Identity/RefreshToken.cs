using Merfit.Domain.Common;

namespace Merfit.Domain.Entities.Identity;

/// <summary>
/// Stored as a SHA-256 hash of the raw refresh token (never the plaintext token). Supports
/// rotation: on refresh, the current token is revoked and ReplacedByTokenHash points at its
/// successor, forming an auditable chain.
/// </summary>
public sealed class RefreshToken : BaseEntity
{
    public Guid UserId { get; set; }
    public required string TokenHash { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RevokedAt { get; set; }
    public string? ReplacedByTokenHash { get; set; }
    public string? CreatedByIp { get; set; }
    public string? RevokedByIp { get; set; }
    public string? UserAgent { get; set; }

    public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;
    public bool IsRevoked => RevokedAt is not null;
    public bool IsActive => !IsRevoked && !IsExpired;
}
