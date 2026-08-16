using Merfit.Application.Common.Interfaces;
using Merfit.Application.Features.Auth;
using Merfit.Infrastructure.Authentication;
using Merfit.Infrastructure.Caching;
using Merfit.Infrastructure.Common;
using Merfit.Infrastructure.ExternalServices;
using Merfit.Infrastructure.Identity;
using Merfit.Infrastructure.Persistence;
using Merfit.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Merfit.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured.");

        services.AddDbContext<MerfitDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.EnableRetryOnFailure(maxRetryCount: 3)));

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));

        services.AddMemoryCache();

        // Common
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
        services.AddSingleton<IPasswordHasher, PasswordHasherService>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<ICacheService, MemoryCacheService>();
        services.AddScoped<IEmailSender, LoggingEmailSender>();

        // Auth
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IUserTokenRepository, UserTokenRepository>();
        services.AddScoped<IUserProvisioningRepository, UserProvisioningRepository>();

        return services;
    }
}
