using MerfitApi.Business.Dtos.Auth;
using MerfitApi.Business.Interfaces.Services;
using Microsoft.AspNetCore.Mvc;

namespace MerfitApi.Api.Controllers;

/// <summary>
/// Kimlik dogrulama uc noktalarini (register, login, refresh vb.) barindirir.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    /// <summary>
    /// Yeni bir MERFIT hesabi olusturur (MerfitNativeApp onboarding akisinin son adimi).
    /// Basarili oldugunda dogrudan kullanilabilir bir access/refresh token cifti doner,
    /// boylece mobil uygulama kayittan sonra ayrica login yapmadan dashboard'a gecebilir.
    /// </summary>
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register([FromBody] RegisterRequest request, CancellationToken cancellationToken)
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var result = await _authService.RegisterAsync(request, ipAddress, cancellationToken);

        return StatusCode(StatusCodes.Status201Created, result);
    }
}