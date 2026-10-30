using MerfitApi.Business.Dtos.Admin.Auth;
using MerfitApi.Business.Interfaces.Services.Admin;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Exceptions;
using MerfitApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitApi.Business.Services.Admin;

/// <summary>IAdminAuthService'in varsayilan implementasyonu.</summary>
public class AdminAuthService : IAdminAuthService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogService _auditLogService;

    public AdminAuthService(IUnitOfWork unitOfWork, IAuditLogService auditLogService)
    {
        _unitOfWork = unitOfWork;
        _auditLogService = auditLogService;
    }

    public async Task ChangePasswordAsync(long adminUserId, AdminChangePasswordRequest request, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var userRepo = _unitOfWork.Repository<ApplicationUser>();
        var user = await userRepo.GetQueryable(asNoTracking: false).FirstOrDefaultAsync(u => u.Id == adminUserId, cancellationToken)
            ?? throw new NotFoundException(nameof(ApplicationUser), adminUserId);

        // Google ile acilmis hesaplarda parola olmayabilir; bos hash BCrypt.Verify'da hata firlatir.
        if (string.IsNullOrEmpty(user.PasswordHash) || !BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
        {
            throw new AppValidationException(nameof(request.CurrentPassword), "Mevcut parola hatali.");
        }

        if (request.NewPassword != request.ConfirmNewPassword)
        {
            throw new AppValidationException(nameof(request.ConfirmNewPassword), "Yeni parolalar eslesmiyor.");
        }

        if (request.NewPassword == request.CurrentPassword)
        {
            throw new AppValidationException(nameof(request.NewPassword), "Yeni parola mevcut paroladan farkli olmalidir.");
        }

        var now = DateTime.UtcNow;
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        user.UpdatedAt = now;
        user.UpdatedUser = adminUserId;
        userRepo.Update(user);

        var tokenRepo = _unitOfWork.Repository<UserRefreshToken>();
        var activeTokens = await tokenRepo.GetQueryable(asNoTracking: false)
            .Where(t => t.UserId == adminUserId && !t.IsRevoked)
            .ToListAsync(cancellationToken);
        foreach (var token in activeTokens)
        {
            token.IsRevoked = true;
            token.RevokedAt = now;
            token.RevokedByIp = ipAddress;
            tokenRepo.Update(token);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogAsync(adminUserId, "ADMIN_PASSWORD_CHANGED", nameof(ApplicationUser), adminUserId, ipAddress, userAgent, cancellationToken: cancellationToken);
    }
}
