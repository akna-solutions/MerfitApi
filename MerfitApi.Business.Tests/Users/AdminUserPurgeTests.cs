using MerfitApi.Business.Dtos.Admin.Users;
using MerfitApi.Business.Interfaces.Services.Admin;
using MerfitApi.Business.Services.Admin;
using MerfitApi.Business.Tests.TestHelpers;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Entities.Enums;
using MerfitApi.Domain.Exceptions;
using MerfitApi.Domain.Interfaces;
using Moq;
using Xunit;

namespace MerfitApi.Business.Tests.Users;

public class AdminUserPurgeTests
{
    private readonly InMemoryUnitOfWork _db = new();
    private readonly Mock<IAuditLogService> _audit = new();
    private readonly Mock<IUserDataPurger> _purger = new();
    private readonly AdminUserService _service;

    public AdminUserPurgeTests()
    {
        _service = new AdminUserService(_db.Object, _audit.Object, _purger.Object);
        _db.Set<ApplicationUser>().AddRange(new[]
        {
            new ApplicationUser { Id = 1, Email = "admin@merfit.app", Role = UserRole.Admin, IsActive = true },
            new ApplicationUser { Id = 2, Email = "root@merfit.app", Role = UserRole.SuperAdmin, IsActive = true },
            new ApplicationUser { Id = 7, Email = "Uye@Example.com", Role = UserRole.User, IsActive = true },
            new ApplicationUser { Id = 8, Email = "other-admin@merfit.app", Role = UserRole.Admin, IsActive = true },
        });
    }

    private static AdminPurgeUserRequest Confirm(string email) => new() { ConfirmEmail = email, Reason = "KVKK talebi" };

    [Fact]
    public async Task Purges_user_and_writes_audit_log_when_email_matches_case_insensitively()
    {
        await _service.PurgeAsync(7, Confirm("  uye@example.com "), 1, "1.1.1.1", "ua");

        _purger.Verify(p => p.PurgeAsync(7, It.IsAny<CancellationToken>()), Times.Once);
        _audit.Verify(a => a.LogAsync(1, "USER_PURGED", nameof(ApplicationUser), 7, "1.1.1.1", "ua",
            It.IsNotNull<object?>(), null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Rejects_mismatched_confirmation_email()
    {
        await Assert.ThrowsAsync<AppValidationException>(() => _service.PurgeAsync(7, Confirm("baska@example.com"), 1, null, null));

        _purger.VerifyNoOtherCalls();
        _audit.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Admin_cannot_purge_own_account()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.PurgeAsync(1, Confirm("admin@merfit.app"), 1, null, null));

        _purger.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Only_super_admin_can_purge_admin_accounts()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.PurgeAsync(8, Confirm("other-admin@merfit.app"), 1, null, null));
        _purger.VerifyNoOtherCalls();

        await _service.PurgeAsync(8, Confirm("other-admin@merfit.app"), 2, null, null);
        _purger.Verify(p => p.PurgeAsync(8, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Unknown_user_throws_not_found()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _service.PurgeAsync(99, Confirm("x@y.com"), 1, null, null));
    }
}
