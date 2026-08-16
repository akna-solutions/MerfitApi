using Merfit.Application.Common.Interfaces;
using Merfit.Application.Common.Models;
using Merfit.Application.Common.Options;
using Merfit.Domain.Constants;
using Merfit.Domain.Entities.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Merfit.Application.Features.Auth;

public sealed class AuthService(
    IUserRepository userRepository,
    IRefreshTokenRepository refreshTokenRepository,
    IUserTokenRepository userTokenRepository,
    IUserProvisioningRepository provisioningRepository,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    IEmailSender emailSender,
    IMembershipProvider membershipProvider,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock,
    IOptions<AuthOptions> options,
    ILogger<AuthService> logger) : IAuthService
{
    private readonly AuthOptions _options = options.Value;

    public async Task<Result<AuthTokensResponse>> RegisterAsync(RegisterRequest request, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = request.Email.Trim().ToUpperInvariant();

        if (await userRepository.ExistsByNormalizedEmailAsync(normalizedEmail, cancellationToken))
        {
            return Result<AuthTokensResponse>.Failure("An account with this email already exists.", "email_taken");
        }

        if (await userRepository.ExistsByUsernameAsync(request.Username, cancellationToken))
        {
            return Result<AuthTokensResponse>.Failure("This username is already taken.", "username_taken");
        }

        var user = new User
        {
            Email = request.Email.Trim(),
            NormalizedEmail = normalizedEmail,
            Username = request.Username.Trim(),
            PasswordHash = passwordHasher.Hash(request.Password),
            FirstName = request.FirstName,
            LastName = request.LastName,
            IsEmailVerified = false,
            IsActive = true,
        };

        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await userRepository.AddAsync(user, ct);
            await userRepository.AssignRoleAsync(user.Id, RoleNames.User, ct);
            await provisioningRepository.InitializeDefaultsAsync(user.Id, ct);
            await unitOfWork.SaveChangesAsync(ct);
        }, cancellationToken);

        await IssueVerificationEmailAsync(user, cancellationToken);

        logger.LogInformation("New user registered {UserId}", user.Id);

        return Result<AuthTokensResponse>.Success(await IssueTokensAsync(user, ipAddress, userAgent, cancellationToken));
    }

    public async Task<Result<AuthTokensResponse>> LoginAsync(LoginRequest request, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = request.Email.Trim().ToUpperInvariant();
        var user = await userRepository.GetByNormalizedEmailAsync(normalizedEmail, cancellationToken);

        if (user is null || !user.IsActive)
        {
            return Result<AuthTokensResponse>.Failure("Invalid email or password.", "invalid_credentials");
        }

        if (!passwordHasher.Verify(user.PasswordHash, request.Password, out var needsRehash))
        {
            return Result<AuthTokensResponse>.Failure("Invalid email or password.", "invalid_credentials");
        }

        if (needsRehash)
        {
            user.PasswordHash = passwordHasher.Hash(request.Password);
        }

        user.LastLoginAt = clock.UtcNow;
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<AuthTokensResponse>.Success(await IssueTokensAsync(user, ipAddress, userAgent, cancellationToken));
    }

    public async Task<Result<AuthTokensResponse>> RefreshAsync(string rawRefreshToken, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var tokenHash = tokenService.HashToken(rawRefreshToken);
        var existing = await refreshTokenRepository.GetByHashAsync(tokenHash, cancellationToken);

        if (existing is null || !existing.IsActive)
        {
            return Result<AuthTokensResponse>.Failure("Refresh token is invalid or has expired.", "invalid_refresh_token");
        }

        var user = await userRepository.GetByIdAsync(existing.UserId, cancellationToken);
        if (user is null || !user.IsActive)
        {
            return Result<AuthTokensResponse>.Failure("Refresh token is invalid or has expired.", "invalid_refresh_token");
        }

        // Rotation: revoke the presented token and mint a brand new one, chained via ReplacedByTokenHash.
        var newRawToken = tokenService.GenerateRefreshTokenValue();
        var newTokenHash = tokenService.HashToken(newRawToken);

        existing.RevokedAt = clock.UtcNow;
        existing.RevokedByIp = ipAddress;
        existing.ReplacedByTokenHash = newTokenHash;
        refreshTokenRepository.Update(existing);

        var newExpiresAt = clock.UtcNow.AddDays(_options.RefreshTokenDays);
        await refreshTokenRepository.AddAsync(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = newTokenHash,
            ExpiresAt = newExpiresAt,
            CreatedByIp = ipAddress,
            UserAgent = userAgent,
        }, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var roles = await userRepository.GetRoleNamesAsync(user.Id, cancellationToken);
        var membership = await membershipProvider.GetMembershipAsync(user.Id, cancellationToken);
        var access = tokenService.GenerateAccessToken(user, roles, membership);

        return Result<AuthTokensResponse>.Success(new AuthTokensResponse(access.Token, access.ExpiresAt, newRawToken, newExpiresAt));
    }

    public async Task<Result> LogoutAsync(string rawRefreshToken, CancellationToken cancellationToken = default)
    {
        var tokenHash = tokenService.HashToken(rawRefreshToken);
        var existing = await refreshTokenRepository.GetByHashAsync(tokenHash, cancellationToken);

        if (existing is not null && existing.IsActive)
        {
            existing.RevokedAt = clock.UtcNow;
            refreshTokenRepository.Update(existing);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result.Success();
    }

    public async Task<Result> LogoutAllAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var activeTokens = await refreshTokenRepository.GetActiveByUserIdAsync(userId, cancellationToken);
        foreach (var token in activeTokens)
        {
            token.RevokedAt = clock.UtcNow;
            refreshTokenRepository.Update(token);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<CurrentUserResponse>> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return Result<CurrentUserResponse>.Failure("User not found.", "not_found");
        }

        var roles = await userRepository.GetRoleNamesAsync(user.Id, cancellationToken);
        var membership = await membershipProvider.GetMembershipAsync(user.Id, cancellationToken);

        return Result<CurrentUserResponse>.Success(new CurrentUserResponse(
            user.Id,
            user.Email,
            user.Username,
            user.FirstName,
            user.LastName,
            user.AvatarUrl,
            user.IsEmailVerified,
            membership,
            roles,
            OnboardingCompleted: false));
    }

    public async Task<Result> VerifyEmailAsync(string rawToken, CancellationToken cancellationToken = default)
    {
        var tokenHash = tokenService.HashToken(rawToken);
        var token = await userTokenRepository.GetByHashAsync(tokenHash, UserTokenPurpose.EmailVerification, cancellationToken);

        if (token is null || !token.IsValid)
        {
            return Result.Failure("This verification link is invalid or has expired.", "invalid_token");
        }

        var user = await userRepository.GetByIdAsync(token.UserId, cancellationToken);
        if (user is null)
        {
            return Result.Failure("This verification link is invalid or has expired.", "invalid_token");
        }

        user.IsEmailVerified = true;
        token.UsedAt = clock.UtcNow;
        userTokenRepository.Update(token);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> ResendVerificationAsync(string email, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByNormalizedEmailAsync(email.Trim().ToUpperInvariant(), cancellationToken);

        // Always return success shape regardless of whether the account exists, to avoid leaking account existence.
        if (user is not null && !user.IsEmailVerified)
        {
            await IssueVerificationEmailAsync(user, cancellationToken);
        }

        return Result.Success();
    }

    public async Task<Result> ForgotPasswordAsync(string email, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByNormalizedEmailAsync(email.Trim().ToUpperInvariant(), cancellationToken);

        if (user is not null)
        {
            var rawToken = tokenService.GenerateRefreshTokenValue();
            await userTokenRepository.AddAsync(new UserToken
            {
                UserId = user.Id,
                TokenHash = tokenService.HashToken(rawToken),
                Purpose = UserTokenPurpose.PasswordReset,
                ExpiresAt = clock.UtcNow.AddHours(_options.PasswordResetTokenHours),
            }, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            await emailSender.SendAsync(
                user.Email,
                "Reset your Merfit password",
                $"<p>Use this code to reset your password: <strong>{rawToken}</strong>. It expires in {_options.PasswordResetTokenHours} hour(s).</p>",
                cancellationToken);
        }

        return Result.Success();
    }

    public async Task<Result> ResetPasswordAsync(string rawToken, string newPassword, CancellationToken cancellationToken = default)
    {
        var tokenHash = tokenService.HashToken(rawToken);
        var token = await userTokenRepository.GetByHashAsync(tokenHash, UserTokenPurpose.PasswordReset, cancellationToken);

        if (token is null || !token.IsValid)
        {
            return Result.Failure("This reset link is invalid or has expired.", "invalid_token");
        }

        var user = await userRepository.GetByIdAsync(token.UserId, cancellationToken);
        if (user is null)
        {
            return Result.Failure("This reset link is invalid or has expired.", "invalid_token");
        }

        user.PasswordHash = passwordHasher.Hash(newPassword);
        token.UsedAt = clock.UtcNow;
        userTokenRepository.Update(token);

        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await unitOfWork.SaveChangesAsync(ct);
            await LogoutAllAsync(user.Id, ct);
        }, cancellationToken);

        return Result.Success();
    }

    private async Task IssueVerificationEmailAsync(User user, CancellationToken cancellationToken)
    {
        var rawToken = tokenService.GenerateRefreshTokenValue();
        await userTokenRepository.AddAsync(new UserToken
        {
            UserId = user.Id,
            TokenHash = tokenService.HashToken(rawToken),
            Purpose = UserTokenPurpose.EmailVerification,
            ExpiresAt = clock.UtcNow.AddHours(_options.EmailVerificationTokenHours),
        }, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await emailSender.SendAsync(
            user.Email,
            "Verify your Merfit account",
            $"<p>Use this code to verify your email: <strong>{rawToken}</strong>. It expires in {_options.EmailVerificationTokenHours} hour(s).</p>",
            cancellationToken);
    }

    private async Task<AuthTokensResponse> IssueTokensAsync(User user, string? ipAddress, string? userAgent, CancellationToken cancellationToken)
    {
        var roles = await userRepository.GetRoleNamesAsync(user.Id, cancellationToken);
        var membership = await membershipProvider.GetMembershipAsync(user.Id, cancellationToken);
        var access = tokenService.GenerateAccessToken(user, roles, membership);

        var rawRefreshToken = tokenService.GenerateRefreshTokenValue();
        var refreshExpiresAt = clock.UtcNow.AddDays(_options.RefreshTokenDays);

        await refreshTokenRepository.AddAsync(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = tokenService.HashToken(rawRefreshToken),
            ExpiresAt = refreshExpiresAt,
            CreatedByIp = ipAddress,
            UserAgent = userAgent,
        }, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new AuthTokensResponse(access.Token, access.ExpiresAt, rawRefreshToken, refreshExpiresAt);
    }
}
