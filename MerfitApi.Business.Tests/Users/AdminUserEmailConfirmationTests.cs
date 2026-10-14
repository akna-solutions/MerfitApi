using MerfitApi.Business.Dtos.Admin.Users;
using MerfitApi.Business.Interfaces.Services.Admin;
using MerfitApi.Business.Services.Admin;
using MerfitApi.Business.Tests.TestHelpers;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Exceptions;
using Moq;
using Xunit;

namespace MerfitApi.Business.Tests.Users;

public class AdminUserEmailConfirmationTests
{
    private readonly InMemoryUnitOfWork _db = new();
    private readonly Mock<IAuditLogService> _audit = new();
    private readonly AdminUserService _service;

    public AdminUserEmailConfirmationTests()
    {
        _service = new AdminUserService(_db.Object, _audit.Object);
    }

    private ApplicationUser AddUser(bool emailConfirmed)
    {
        var user = new ApplicationUser { Id = 7, Email = "a@b.com", EmailConfirmed = emailConfirmed, IsActive = true };
        _db.Set<ApplicationUser>().Add(user);
        return user;
    }

    [Fact]
    public async Task Marks_email_as_confirmed_and_writes_audit_log()
    {
        var user = AddUser(emailConfirmed: false);

        await _service.UpdateEmailConfirmationAsync(7, new AdminUpdateUserEmailConfirmationRequest { EmailConfirmed = true, Reason = "Kod ulasmadi" }, 1, "1.1.1.1", "ua");

        Assert.True(user.EmailConfirmed);
        Assert.Equal(1L, user.UpdatedUser);
        Assert.Equal(1, _db.SaveCount);
        _audit.Verify(a => a.LogAsync(1, "USER_EMAIL_CONFIRMATION_CHANGED", nameof(ApplicationUser), 7, "1.1.1.1", "ua",
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Can_revoke_confirmation()
    {
        var user = AddUser(emailConfirmed: true);

        await _service.UpdateEmailConfirmationAsync(7, new AdminUpdateUserEmailConfirmationRequest { EmailConfirmed = false }, 1, null, null);

        Assert.False(user.EmailConfirmed);
        Assert.Equal(1, _db.SaveCount);
    }

    [Fact]
    public async Task Same_value_is_a_no_op_without_audit_entry()
    {
        AddUser(emailConfirmed: true);

        await _service.UpdateEmailConfirmationAsync(7, new AdminUpdateUserEmailConfirmationRequest { EmailConfirmed = true }, 1, null, null);

        Assert.Equal(0, _db.SaveCount);
        _audit.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Unknown_user_throws_not_found()
    {
        _db.Set<ApplicationUser>();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.UpdateEmailConfirmationAsync(99, new AdminUpdateUserEmailConfirmationRequest { EmailConfirmed = true }, 1, null, null));
    }
}
