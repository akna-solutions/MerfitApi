using Merfit.Application.Features.Auth;
using Merfit.Domain.Entities.Notifications;
using Merfit.Domain.Entities.Profile;
using Merfit.Domain.Entities.Scoring;
using Merfit.Domain.Entities.WorkoutSessions;
using Merfit.Infrastructure.Persistence;

namespace Merfit.Infrastructure.Repositories;

public sealed class UserProvisioningRepository(MerfitDbContext db) : IUserProvisioningRepository
{
    public async Task InitializeDefaultsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await db.UserProfiles.AddAsync(new UserProfile { UserId = userId }, cancellationToken);
        await db.UserSettings.AddAsync(new UserSettings { UserId = userId }, cancellationToken);
        await db.NotificationPreferences.AddAsync(new NotificationPreference { UserId = userId }, cancellationToken);
        await db.UserWorkoutStreaks.AddAsync(new UserWorkoutStreak { UserId = userId }, cancellationToken);
        await db.UserScores.AddAsync(new UserScore { UserId = userId }, cancellationToken);
    }
}
