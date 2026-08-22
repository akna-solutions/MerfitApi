using System.ComponentModel.DataAnnotations;

namespace MerfitApi.Business.Dtos.Auth;

/// <summary>
/// Giris (login) istegi icin kullanilan DTO.
/// </summary>
public class LoginRequest
{
    /// <summary>Kullanicinin e-posta adresi.</summary>
    [Required(ErrorMessage = "E-posta alani zorunludur.")]
    [EmailAddress(ErrorMessage = "Gecerli bir e-posta adresi giriniz.")]
    public string Email { get; set; } = string.Empty;

    /// <summary>Kullanicinin parolasi.</summary>
    [Required(ErrorMessage = "Parola alani zorunludur.")]
    public string Password { get; set; } = string.Empty;
}
