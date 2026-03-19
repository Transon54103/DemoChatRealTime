using System.ComponentModel.DataAnnotations;

namespace DemoChatRealTime.Models.Entities;

/// <summary>
/// NOTE - Entity User:
/// - Dùng Salt + Hash ?? l?u password thay vì plain text (b?o m?t c? b?n).
/// - Trong production nên dùng ASP.NET Identity ho?c IdentityServer/Duende cho ??y ?? tính n?ng
///   (2FA, lockout, email confirm, external login...).
/// - DisplayName tách riêng kh?i Username ?? linh ho?t hi?n th?.
/// - CreatedAt giúp audit trail - bi?t user t?o khi nào.
/// </summary>
public class AppUser
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string Username { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// NOTE: Không bao gi? l?u plain-text password.
    /// Dùng HMACSHA512 t?o hash + salt.
    /// Production nên dùng BCrypt ho?c Argon2.
    /// </summary>
    [Required]
    public byte[] PasswordHash { get; set; } = Array.Empty<byte>();

    [Required]
    public byte[] PasswordSalt { get; set; } = Array.Empty<byte>();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
    public ICollection<ChatRoomMember> ChatRoomMembers { get; set; } = new List<ChatRoomMember>();
}
