using MerfitApi.Business.Common;
using MerfitApi.Business.Dtos.Auth;
using MerfitApi.Business.Interfaces.Services;
using MerfitApi.Business.Services.Auth;
using MerfitApi.Business.Tests.TestHelpers;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Exceptions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace MerfitApi.Business.Tests.Auth;

public class AuthRefreshTests
{
    private readonly InMemoryUnitOfWork _db = new();
    private readonly Mock<ITokenService> _tokens = new();
    private readonly AuthService _service;
    private int _issued;

    public AuthRefreshTests()
    {
        _tokens.Setup(t => t.GenerateAccessToken(It.IsAny<ApplicationUser>())).Returns(("access", DateTime.UtcNow.AddMinutes(60)));
        _tokens.Setup(t => t.GenerateRefreshToken()).Returns(() => $"new-{++_issued}");
        _service = new AuthService(_db.Object, _tokens.Object, Options.Create(new JwtSettings { RefreshTokenExpirationDays = 30 }));

        _db.Set<ApplicationUser>().Add(new ApplicationUser { Id = 1, Email = "admin@merfit.app", IsActive = true });
        _db.Set<UserProfile>().Add(new UserProfile { Id = 1, UserId = 1, FirstName = "Ada", LastName = "Yilmaz" });
        _db.Set<UserRefreshToken>().AddRange(new[]
        {
            new UserRefreshToken { Id = 10, UserId = 1, Token = "valid", ExpiresAt = DateTime.UtcNow.AddDays(5) },
            new UserRefreshToken { Id = 11, UserId = 1, Token = "other-device", ExpiresAt = DateTime.UtcNow.AddDays(5) },
            new UserRefreshToken { Id = 12, UserId = 1, Token = "expired", ExpiresAt = DateTime.UtcNow.AddMinutes(-1) },
        });
    }

    private Task<AuthResponse> Refresh(string token) => _service.RefreshAsync(new RefreshTokenRequest { RefreshToken = token }, "1.1.1.1");

    private UserRefreshToken Token(string value) => _db.Set<UserRefreshToken>().Single(t => t.Token == value);

    [Fact]
    public async Task Rotates_the_refresh_token_and_returns_a_new_pair()
    {
        var result = await Refresh("valid");

        Assert.Equal("access", result.AccessToken);
        Assert.Equal("new-1", result.RefreshToken);
        Assert.Equal("Ada Yilmaz", result.Name);
        Assert.True(Token("valid").IsUsed);
        Assert.True(Token("valid").IsRevoked);
        Assert.Equal("new-1", Token("valid").ReplacementToken);
        Assert.False(Token("new-1").IsRevoked);
        Assert.False(Token("other-device").IsRevoked);
    }

    [Fact]
    public async Task Reusing_a_rotated_token_revokes_every_session_of_the_user()
    {
        await Refresh("valid");

        await Assert.ThrowsAsync<UnauthorizedException>(() => Refresh("valid"));

        Assert.True(Token("new-1").IsRevoked);
        Assert.True(Token("other-device").IsRevoked);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("unknown")]
    public async Task Expired_or_unknown_tokens_are_rejected(string token)
    {
        await Assert.ThrowsAsync<UnauthorizedException>(() => Refresh(token));
        Assert.Equal(0, _issued);
    }

    [Fact]
    public async Task Inactive_or_deleted_users_cannot_refresh()
    {
        _db.Set<ApplicationUser>().Single().DeletedAt = DateTime.UtcNow;

        await Assert.ThrowsAsync<UnauthorizedException>(() => Refresh("valid"));
        Assert.False(Token("valid").IsUsed);
    }
}
