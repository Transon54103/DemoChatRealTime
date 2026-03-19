using System.ComponentModel.DataAnnotations;

namespace DemoChatRealTime.Models.DTOs;

/// <summary>
/// NOTE - DTO Pattern:
/// - Tách bi?t Entity (DB) và DTO (API/View) ? không expose thông tin nh?y c?m (password hash...).
/// - Validate ? DTO level (DataAnnotations) tr??c khi ch?m service/DB.
/// - Trong production nên dùng FluentValidation cho complex validation rules.
/// </summary>

public class LoginDto
{
    [Required(ErrorMessage = "Username là b?t bu?c")]
    [MaxLength(50)]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password là b?t bu?c")]
    [MinLength(4, ErrorMessage = "Password ít nh?t 4 ký t?")]
    public string Password { get; set; } = string.Empty;
}

public class RegisterDto
{
    [Required(ErrorMessage = "Username là b?t bu?c")]
    [MaxLength(50)]
    [RegularExpression(@"^[a-zA-Z0-9_]+$", ErrorMessage = "Username ch? ch?a ch?, s? và _")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Display Name là b?t bu?c")]
    [MaxLength(100)]
    public string DisplayName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password là b?t bu?c")]
    [MinLength(4, ErrorMessage = "Password ít nh?t 4 ký t?")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Xác nh?n password là b?t bu?c")]
    [Compare("Password", ErrorMessage = "Password không kh?p")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public class ChatMessageDto
{
    public int Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public string MessageType { get; set; } = "Text";
    public DateTime SentAt { get; set; }
    public int SenderId { get; set; }
    public string SenderName { get; set; } = string.Empty;
    public int ChatRoomId { get; set; }
}

public class SendMessageDto
{
    [Required]
    [MaxLength(2000, ErrorMessage = "Tin nh?n t?i ?a 2000 ký t?")]
    public string Content { get; set; } = string.Empty;

    [Required]
    public int ChatRoomId { get; set; }
}

public class ChatRoomDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsGroupChat { get; set; }
    public int MemberCount { get; set; }
    public string? LastMessage { get; set; }
    public DateTime? LastMessageAt { get; set; }
}

public class CreateRoomDto
{
    [Required(ErrorMessage = "Tên phòng là b?t bu?c")]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;
}

public class UserOnlineDto
{
    public int UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public bool IsOnline { get; set; }
}
