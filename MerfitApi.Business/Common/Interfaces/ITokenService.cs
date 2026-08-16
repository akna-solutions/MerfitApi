using Merfit.Domain.Entities.Identity;

namespace Merfit.Application.Common.Interfaces;

public sealed record AccessTokenResult(string Token, DateTimeOffset ExpiresAt);

public interface ITokenService
{
    /// <summary>Generates a short-lived JWT access token. Membership is embedded only as a UX hint (jti/sub/email/name/role/membership claims) — never trusted as the sole authority for entitlement checks server-side.</summary>
    AccessTokenResult GenerateAccessToken(User user, IReadOnlyCollection<string> roles, string membership);

    /// <summary>Generates a cryptographically random opaque refresh token (the raw value returned to the client; only its hash is ever persisted).</summary>
    string GenerateRefreshTokenValue();

    string HashToken(string rawToken);
}
