using MerfitApi.Api.Extensions;
using MerfitApi.Business.Common.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MerfitApi.Api.Controllers.Admin;

/// <summary>
/// Tum Admin API controller'larinin turedigi temel sinif.
/// - "AdminOnly" policy'si ile korunur (bkz. Program.cs); yetkisiz/kimliksiz istekler
///   framework tarafindan otomatik olarak 401/403 ile reddedilir; hicbir admin action'i
///   bu koruma disinda birakilmamalidir.
/// - Ortak ApiResponse/PagedResult zarfini uretmek ve mevcut admin kullaniciyi
///   (audit log icin) okumak icin yardimci metotlar saglar.
/// </summary>
[ApiController]
[Authorize(Policy = "AdminOnly")]
[Produces("application/json")]
public abstract class AdminControllerBase : ControllerBase
{
    /// <summary>Istegi yapan admin kullanicinin Id'si (JWT "sub" claim'i).</summary>
    protected long? CurrentAdminId => User.GetUserId();

    /// <summary>Istegi yapan admin kullanicinin e-posta adresi.</summary>
    protected string? CurrentAdminEmail => User.GetEmail();

    /// <summary>Audit log'a yazilacak istemci IP adresi.</summary>
    protected string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();

    /// <summary>Audit log'a yazilacak istemci User-Agent bilgisi.</summary>
    protected string? UserAgent => Request.Headers.UserAgent.ToString();

    /// <summary>200 OK + ApiResponse&lt;T&gt; zarfi.</summary>
    protected ActionResult<ApiResponse<T>> SuccessResponse<T>(T data) => Ok(ApiResponse<T>.Success(data));

    /// <summary>201 Created + ApiResponse&lt;T&gt; zarfi.</summary>
    protected ActionResult<ApiResponse<T>> CreatedResponse<T>(T data) =>
        StatusCode(StatusCodes.Status201Created, ApiResponse<T>.Success(data));

    /// <summary>Govde donmeyen basarili islemler icin 200 OK + ApiResponse zarfi.</summary>
    protected ActionResult<ApiResponse> SuccessResponse() => Ok(ApiResponse.Success());
}
