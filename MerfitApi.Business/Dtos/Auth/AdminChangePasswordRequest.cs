using System.ComponentModel.DataAnnotations;

namespace MerfitApi.Business.Dtos.Admin.Auth;

/// <summary>POST /api/admin/auth/change-password govdesi; giris yapmis admin kendi parolasini degistirir.</summary>
public class AdminChangePasswordRequest
{
    [Required(ErrorMessage = "Mevcut parola zorunludur.")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Yeni parola zorunludur.")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Yeni parola 8-100 karakter arasinda olmalidir.")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Yeni parola tekrari zorunludur.")]
    public string ConfirmNewPassword { get; set; } = string.Empty;
}
