namespace Merfit.Application.Common.Options;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public int RefreshTokenDays { get; init; } = 30;
    public int EmailVerificationTokenHours { get; init; } = 24;
    public int PasswordResetTokenHours { get; init; } = 1;
    public int MaxActiveRefreshTokensPerUser { get; init; } = 10;
}
