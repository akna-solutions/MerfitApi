using MerfitApi.Business.Dtos.Admin.Auth;
using MerfitApi.Business.Interfaces.Services.Admin;
using MerfitApi.Business.Services.Admin;
using MerfitApi.Business.Tests.TestHelpers;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Entities.Enums;
using MerfitApi.Domain.Exceptions;
using Moq;
using Xunit;

namespace MerfitApi.Business.Tests.Auth;

public class AdminChangePasswordTests
{
    private const string CurrentPassword = "eski-parola-123";

    private readonly InMemoryUnitOfWork _db = new();
    private readonly Mock<IAuditLogService> _audit = new();
    private readonly AdminAuthService _service;
    private readonly ApplicationUser _admin;

    public AdminChangePasswordTests()
    {
        _service = new AdminAuthService(_db.Object, _audit.Object);
        _admin = new ApplicationUser
        {
            Id = 1, Email = "admin@merfit.app", Role = UserRole.Admin, IsActive = true,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(CurrentPassword),
        };
        _db.Set<ApplicationUser>().Add(_admin);
        _db.Set<UserRefreshToken>().AddRange(new[]
        {
            new UserRefreshToken { Id = 10, UserId = 1, Token = "a", ExpiresAt = DateTime.UtcNow.AddDays(7) },
            new UserRefreshToken { Id = 11, UserId = 1, Token = "b", ExpiresAt = DateTime.UtcNow.AddDays(7) },
            new UserRefreshToken { Id = 12, UserId = 5, Token = "c", ExpiresAt = DateTime.UtcNow.AddDays(7) },
        });
    }

    private static AdminChangePasswordRequest Request(string current, string next, string? confirm = null) =>
        new() { CurrentPassword = current, NewPassword = next, ConfirmNewPassword = confirm ?? next };

    [Fact]
    public async Task Changes_password_revokes_own_refresh_tokens_and_writes_audit_log()
    {
        await _service.ChangePasswordAsync(1, Request(CurrentPassword, "yeni-parola-456"), "1.1.1.1", "ua");

        Assert.True(BCrypt.Net.BCrypt.Verify("yeni-parola-456", _admin.PasswordHash));
        Assert.False(BCrypt.Net.BCrypt.Verify(CurrentPassword, _admin.PasswordHash));
        var tokens = _db.Set<UserRefreshToken>();
        Assert.All(tokens.Where(t => t.UserId == 1), t => Assert.True(t.IsRevoked));
        Assert.False(tokens.Single(t => t.UserId == 5).IsRevoked);
        Assert.Equal(1, _db.SaveCount);
        _audit.Verify(a => a.LogAsync(1, "ADMIN_PASSWORD_CHANGED", nameof(ApplicationUser), 1, "1.1.1.1", "ua",
            null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Wrong_current_password_is_rejected()
    {
        var ex = await Assert.ThrowsAsync<AppValidationException>(() =>
            _service.ChangePasswordAsync(1, Request("yanlis", "yeni-parola-456"), null, null));

        Assert.Contains(nameof(AdminChangePasswordRequest.CurrentPassword), ex.Errors.Keys);
        Assert.True(BCrypt.Net.BCrypt.Verify(CurrentPassword, _admin.PasswordHash));
        Assert.Equal(0, _db.SaveCount);
    }

    [Fact]
    public async Task Mismatched_confirmation_is_rejected()
    {
        await Assert.ThrowsAsync<AppValidationException>(() =>
            _service.ChangePasswordAsync(1, Request(CurrentPassword, "yeni-parola-456", "baska-parola-789"), null, null));

        Assert.Equal(0, _db.SaveCount);
    }

    [Fact]
    public async Task New_password_must_differ_from_current()
    {
        await Assert.ThrowsAsync<AppValidationException>(() =>
            _service.ChangePasswordAsync(1, Request(CurrentPassword, CurrentPassword), null, null));
    }

    [Fact]
    public async Task Account_without_password_cannot_verify_current_password()
    {
        _admin.PasswordHash = string.Empty;

        await Assert.ThrowsAsync<AppValidationException>(() =>
            _service.ChangePasswordAsync(1, Request("herhangi", "yeni-parola-456"), null, null));
    }
}
