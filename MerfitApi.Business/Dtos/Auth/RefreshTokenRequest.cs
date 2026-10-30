using System.ComponentModel.DataAnnotations;

namespace MerfitApi.Business.Dtos.Auth;

/// <summary>POST /api/auth/refresh govdesi.</summary>
public class RefreshTokenRequest
{
    /// <summary>Login/register/refresh yanitinda donen refresh token.</summary>
    [Required(ErrorMessage = "Refresh token zorunludur.")]
    public string RefreshToken { get; set; } = string.Empty;
}
