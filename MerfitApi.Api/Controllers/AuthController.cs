using System.Security.Claims;
using FluentValidation;
using Merfit.Application.Common.Interfaces;
using Merfit.Application.Features.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Merfit.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
[Produces("application/json")]
public sealed class AuthController(
    IAuthService authService,
    ICurrentUserService currentUser,
    IValidator<RegisterRequest> registerValidator,
    IValidator<LoginRequest> loginValidator,
    IValidator<RefreshRequest> refreshValidator,
    IValidator<LogoutRequest> logoutValidator,
    IValidator<ForgotPasswordRequest> forgotPasswordValidator,
    IValidator<ResetPasswordRequest> resetPasswordValidator,
    IValidator<VerifyEmailRequest> verifyEmailValidator,
    IValidator<ResendVerificationRequest> resendVerificationValidator) : ControllerBase
{
    /// <summary>Creates a new account.</summary>
    [HttpPost("register")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(AuthTokensResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken cancellationToken)
    {
        await registerValidator.ValidateAndThrowAsync(request, cancellationToken);
        var result = await authService.RegisterAsync(request, currentUser.IpAddress, currentUser.UserAgent, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : Conflict(new { result.Error, result.ErrorCode });
    }

    /// <summary>Authenticates with email + password.</summary>
    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(AuthTokensResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        await loginValidator.ValidateAndThrowAsync(request, cancellationToken);
        var result = await authService.LoginAsync(request, currentUser.IpAddress, currentUser.UserAgent, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : Unauthorized(new { result.Error, result.ErrorCode });
    }

    /// <summary>Exchanges a valid refresh token for a new access + refresh token pair (rotation).</summary>
    [HttpPost("refresh")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(AuthTokensResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request, CancellationToken cancellationToken)
    {
        await refreshValidator.ValidateAndThrowAsync(request, cancellationToken);
        var result = await authService.RefreshAsync(request.RefreshToken, currentUser.IpAddress, currentUser.UserAgent, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : Unauthorized(new { result.Error, result.ErrorCode });
    }

    /// <summary>Revokes a single refresh token (signs out the current device).</summary>
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest request, CancellationToken cancellationToken)
    {
        await logoutValidator.ValidateAndThrowAsync(request, cancellationToken);
        await authService.LogoutAsync(request.RefreshToken, cancellationToken);
        return NoContent();
    }

    /// <summary>Revokes every active refresh token for the current user (signs out all devices).</summary>
    [HttpPost("logout-all")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> LogoutAll(CancellationToken cancellationToken)
    {
        await authService.LogoutAllAsync(currentUser.UserId!.Value, cancellationToken);
        return NoContent();
    }

    /// <summary>Returns the authenticated user's identity/membership summary.</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(CurrentUserResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        var result = await authService.GetCurrentUserAsync(currentUser.UserId!.Value, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : NotFound(new { result.Error });
    }

    [HttpPost("verify-email")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailRequest request, CancellationToken cancellationToken)
    {
        await verifyEmailValidator.ValidateAndThrowAsync(request, cancellationToken);
        var result = await authService.VerifyEmailAsync(request.Token, cancellationToken);
        return result.Succeeded ? NoContent() : BadRequest(new { result.Error, result.ErrorCode });
    }

    [HttpPost("resend-verification")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ResendVerification([FromBody] ResendVerificationRequest request, CancellationToken cancellationToken)
    {
        await resendVerificationValidator.ValidateAndThrowAsync(request, cancellationToken);
        await authService.ResendVerificationAsync(request.Email, cancellationToken);
        return NoContent();
    }

    [HttpPost("forgot-password")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        await forgotPasswordValidator.ValidateAndThrowAsync(request, cancellationToken);
        await authService.ForgotPasswordAsync(request.Email, cancellationToken);
        return NoContent();
    }

    [HttpPost("reset-password")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        await resetPasswordValidator.ValidateAndThrowAsync(request, cancellationToken);
        var result = await authService.ResetPasswordAsync(request.Token, request.NewPassword, cancellationToken);
        return result.Succeeded ? NoContent() : BadRequest(new { result.Error, result.ErrorCode });
    }
}
