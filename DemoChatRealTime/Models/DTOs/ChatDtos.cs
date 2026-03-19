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
    [Required(ErrorMessage = "Username là bắt buộc")]
    [MaxLength(50)]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password là bắt buộc")]
    [MinLength(4, ErrorMessage = "Password ít nhất 4 ký tự")]
    public string Password { get; set; } = string.Empty;
}

public class RegisterDto
{
    [Required(ErrorMessage = "Username là bắt buộc")]
    [MaxLength(50)]
    [RegularExpression(@"^[a-zA-Z0-9_]+$", ErrorMessage = "Username chỉ chứa chữ, số và _")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Display Name là bắt buộc")]
    [MaxLength(100)]
    public string DisplayName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password là bắt buộc")]
    [MinLength(4, ErrorMessage = "Password ít nhất 4 ký tự")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Xác nhận password là bắt buộc")]
    [Compare("Password", ErrorMessage = "Password không khớp")]
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
    [MaxLength(2000, ErrorMessage = "Tin nhắn tối đa 2000 ký tự")]
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
    [Required(ErrorMessage = "Tên phòng là bắt buộc")]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;
}

public class UserOnlineDto
{
    public int UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public bool IsOnline { get; set; }
}
