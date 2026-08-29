using MerfitApi.Business.Common.Responses;
using MerfitApi.Business.Dtos.Admin.Auth;
using MerfitApi.Domain.Entities;
using MerfitApi.Domain.Exceptions;
using MerfitApi.Domain.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace MerfitApi.Api.Controllers.Admin;

/// <summary>
/// Admin paneli icin kimlik/oturum bilgisi uc noktalari. Login/register islemleri mevcut
/// /api/auth uzerinden (ApplicationUser ortak oldugu icin) yapilir; bu controller sadece
/// gecerli bir admin JWT'si ile girilen "kim olarak giris yapildi" bilgisini doner.
/// </summary>
[Route("api/admin/auth")]
public class AdminAuthController : AdminControllerBase
{
    private readonly IUnitOfWork _unitOfWork;

    public AdminAuthController(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// Gecerli bir admin access token'i ile giris yapmis kullanicinin temel bilgilerini doner.
    /// Admin panelinin uygulama acilisinda oturumu dogrulamak/kullanici bilgisini almak icin kullanilir.
    /// </summary>
    [HttpGet("me")]
    [ProducesResponseType(typeof(ApiResponse<AdminMeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<AdminMeResponse>>> Me(CancellationToken cancellationToken)
    {
        var userId = CurrentAdminId ?? throw new Exception("Gecersiz oturum.");

        var user = await _unitOfWork.Repository<ApplicationUser>().GetByIdAsync(userId, cancellationToken)
            ?? throw new NotFoundException(nameof(ApplicationUser), userId);

        return SuccessResponse(new AdminMeResponse
        {
            UserId = user.Id,
            Email = user.Email,
            Role = user.Role.ToString(),
            LastLoginAt = user.LastLoginAt,
        });
    }
}
