using Merfit.Domain.Entities.Identity;

namespace Merfit.Application.Features.Auth;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<User?> GetByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default);
    Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default);
    Task<bool> ExistsByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default);
    Task<bool> ExistsByUsernameAsync(string username, CancellationToken cancellationToken = default);
    Task AddAsync(User user, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<string>> GetRoleNamesAsync(Guid userId, CancellationToken cancellationToken = default);
    Task AssignRoleAsync(Guid userId, string roleName, CancellationToken cancellationToken = default);
}

public interface IRefreshTokenRepository
{
    Task AddAsync(RefreshToken token, CancellationToken cancellationToken = default);
    Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<RefreshToken>> GetActiveByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
    void Update(RefreshToken token);
}

public interface IUserTokenRepository
{
    Task AddAsync(UserToken token, CancellationToken cancellationToken = default);
    Task<UserToken?> GetByHashAsync(string tokenHash, UserTokenPurpose purpose, CancellationToken cancellationToken = default);
    void Update(UserToken token);
}

/// <summary>Creates the default 1:1 rows (profile, settings, notification preferences, streak, score) every new user needs, in one place so no feature forgets one.</summary>
public interface IUserProvisioningRepository
{
    Task InitializeDefaultsAsync(Guid userId, CancellationToken cancellationToken = default);
}
