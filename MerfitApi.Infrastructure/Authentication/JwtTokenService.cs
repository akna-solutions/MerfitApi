using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Merfit.Application.Common.Interfaces;
using Merfit.Domain.Entities.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;

namespace Merfit.Infrastructure.Authentication;

public sealed class JwtTokenService(IOptions<JwtOptions> options) : ITokenService
{
    private readonly JwtOptions _options = options.Value;

    public AccessTokenResult GenerateAccessToken(User user, IReadOnlyCollection<string> roles, string membership)
    {
        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("name", user.DisplayName),
            new("membership", membership),
        };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var key = new SymmetricSecurityKey(ResolveKeyBytes(_options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: credentials);

        var raw = new JwtSecurityTokenHandler().WriteToken(token);
        return new AccessTokenResult(raw, expires);
    }

    public string GenerateRefreshTokenValue()
    {
        Span<byte> bytes = stackalloc byte[64];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .Replace("=", "");
    }

    public string HashToken(string rawToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(bytes);
    }

    private static byte[] ResolveKeyBytes(string signingKey)
    {
        try
        {
            return Convert.FromBase64String(signingKey);
        }
        catch (FormatException)
        {
            return Encoding.UTF8.GetBytes(signingKey);
        }
    }
}
