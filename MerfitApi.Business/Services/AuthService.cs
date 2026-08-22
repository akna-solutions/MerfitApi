using MerfitApi.Business.Common;
using MerfitApi.Business.Dtos.Auth;
using MerfitApi.Business.Interfaces.Services;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Entities.Enums;
using MerfitApi.Domain.Exceptions;
using MerfitApi.Domain.Interfaces;
using Microsoft.Extensions.Options;

namespace MerfitApi.Business.Services.Auth;

/// <summary>
/// IAuthService'in varsayilan implementasyonu. ApplicationUser + UserProfile kaydini
/// tek bir islemde (transaction) olusturur ve JWT token cifti uretir.
/// </summary>
public class AuthService : IAuthService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITokenService _tokenService;
    private readonly JwtSettings _jwtSettings;

    public AuthService(IUnitOfWork unitOfWork, ITokenService tokenService, IOptions<JwtSettings> jwtSettings)
    {
        _unitOfWork = unitOfWork;
        _tokenService = tokenService;
        _jwtSettings = jwtSettings.Value;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, string? ipAddress)
    {
        ValidateAccountFields(request);

        var normalizedEmail = request.Email.Trim().ToUpperInvariant();

        var userRepo = _unitOfWork.Repository<ApplicationUser>();
        var emailTaken = await userRepo.AnyAsync(u => u.NormalizedEmail == normalizedEmail);
        if (emailTaken)
        {
            throw new ConflictException("Bu e-posta adresi ile kayitli bir hesap zaten mevcut.");
        }

        var heightCm = ResolveHeightCm(request);
        var weightKg = ResolveWeightKg(request);
        var unitSystem = request.HeightUnit.Equals("ft_in", StringComparison.OrdinalIgnoreCase)
            || request.WeightUnit.Equals("lb", StringComparison.OrdinalIgnoreCase)
            ? UnitSystem.Imperial
            : UnitSystem.Metric;

        var (firstName, lastName) = SplitName(request.Name);

        var equipmentIds = (request.EquipmentIds ?? new List<long>()).Distinct().ToList();
        IReadOnlyList<Equipment> equipments = Array.Empty<Equipment>();
        if (equipmentIds.Count > 0)
        {
            var equipmentRepo = _unitOfWork.Repository<Equipment>();
            equipments = await equipmentRepo.FindAsync(e => equipmentIds.Contains(e.Id));
            if (equipments.Count != equipmentIds.Count)
            {
                throw new AppValidationException(nameof(request.EquipmentIds), "Gecersiz ekipman kimligi (id) gonderildi.");
            }
        }

        await _unitOfWork.BeginTransactionAsync();
        var user = new ApplicationUser
        {
            Email = request.Email.Trim(),
            NormalizedEmail = normalizedEmail,
            UserName = request.Email.Trim(),
            NormalizedUserName = normalizedEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            EmailConfirmed = false,
            PhoneNumberConfirmed = false,
            LockoutEnabled = true,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };

        await userRepo.AddAsync(user);
        await _unitOfWork.SaveChangesAsync();

        var username = await GenerateUniqueUsernameAsync(request.Email);

        var profile = new UserProfile
        {
            UserId = user.Id,
            FirstName = firstName,
            LastName = lastName,
            Username = username,
            DateOfBirth = request.Age.HasValue
                ? DateTime.UtcNow.Date.AddYears(-request.Age.Value)
                : null,
            Gender = request.Gender,
            HeightCm = heightCm,
            WeightKg = weightKg,
            Goal = request.Goal,
            ExperienceLevel = request.TrainingExperience,
            ActivityLevel = request.ActivityLevel,
            TrainingLocation = request.TrainingLocation,
            TrainingDaysPerWeek = request.TrainingDays,
            UnitSystem = unitSystem,
            CreatedAt = DateTime.UtcNow,
        };

        await _unitOfWork.Repository<UserProfile>().AddAsync(profile);

        if (equipmentIds.Count > 0)
        {
            var userEquipments = equipmentIds.Select(equipmentId => new UserEquipment
            {
                UserId = user.Id,
                EquipmentId = equipmentId,
                CreatedAt = DateTime.UtcNow,
            });

            await _unitOfWork.Repository<UserEquipment>().AddRangeAsync(userEquipments);
        }

        var (accessToken, expiresAt) = _tokenService.GenerateAccessToken(user);
        var refreshTokenValue = _tokenService.GenerateRefreshToken();

        var refreshToken = new UserRefreshToken
        {
            UserId = user.Id,
            Token = refreshTokenValue,
            ExpiresAt = DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpirationDays),
            CreatedByIp = ipAddress,
            IsRevoked = false,
            IsUsed = false,
            CreatedAt = DateTime.UtcNow,
        };

        await _unitOfWork.Repository<UserRefreshToken>().AddAsync(refreshToken);

        await _unitOfWork.CommitTransactionAsync();

        return new AuthResponse
        {
            UserId = user.Id,
            Email = user.Email,
            Name = request.Name.Trim(),
            AccessToken = accessToken,
            RefreshToken = refreshTokenValue,
            AccessTokenExpiresAt = expiresAt,
        };
    }

    private static void ValidateAccountFields(RegisterRequest request)
    {
        if (request.Password != request.ConfirmPassword)
        {
            throw new AppValidationException(nameof(request.ConfirmPassword), "Parolalar eslesmiyor.");
        }

        if (request.HeightUnit.Equals("cm", StringComparison.OrdinalIgnoreCase) && request.HeightCm is null)
        {
            throw new AppValidationException(nameof(request.HeightCm), "Boy (cm) alani zorunludur.");
        }

        if (request.HeightUnit.Equals("ft_in", StringComparison.OrdinalIgnoreCase) && request.HeightFeet is null)
        {
            throw new AppValidationException(nameof(request.HeightFeet), "Boy (ft/in) alani zorunludur.");
        }
    }

    private static decimal? ResolveHeightCm(RegisterRequest request)
    {
        if (request.HeightUnit.Equals("ft_in", StringComparison.OrdinalIgnoreCase))
        {
            if (request.HeightFeet is null) return null;
            var totalInches = (request.HeightFeet.Value * 12) + (request.HeightInches ?? 0);
            return Math.Round(totalInches * 2.54m, 2);
        }

        return request.HeightCm;
    }

    private static decimal? ResolveWeightKg(RegisterRequest request)
    {
        if (request.Weight is null) return null;

        return request.WeightUnit.Equals("lb", StringComparison.OrdinalIgnoreCase)
            ? Math.Round(request.Weight.Value * 0.453592m, 2)
            : request.Weight;
    }

    private static (string FirstName, string LastName) SplitName(string fullName)
    {
        var trimmed = fullName.Trim();
        var parts = trimmed.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);

        return parts.Length switch
        {
            0 => (string.Empty, string.Empty),
            1 => (parts[0], string.Empty),
            _ => (parts[0], parts[1]),
        };
    }

    /// <summary>
    /// RN onboarding akisi ayrica bir "kullanici adi" toplamadigindan, e-postanin
    /// yerel kismindan (@'den once) benzersiz bir kullanici adi turetir.
    /// </summary>
    private async Task<string> GenerateUniqueUsernameAsync(string email)
    {
        var baseUsername = email.Split('@')[0].Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(baseUsername))
        {
            baseUsername = "user";
        }

        var profileRepo = _unitOfWork.Repository<UserProfile>();
        var candidate = baseUsername;
        var attempt = 0;

        while (await profileRepo.AnyAsync(p => p.Username == candidate))
        {
            attempt++;
            candidate = $"{baseUsername}{Random.Shared.Next(1000, 9999)}";

            if (attempt > 10)
            {
                candidate = $"{baseUsername}{Guid.NewGuid():N}"[..30];
                break;
            }
        }

        return candidate;
    }
}