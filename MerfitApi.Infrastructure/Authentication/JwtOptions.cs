namespace Merfit.Infrastructure.Authentication;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public required string Issuer { get; init; }
    public required string Audience { get; init; }

    /// <summary>Base64 or plain UTF-8 signing secret, minimum 32 bytes. Must be supplied via configuration/user-secrets/environment — never committed.</summary>
    public required string SigningKey { get; init; }

    public int AccessTokenMinutes { get; init; } = 15;
}
