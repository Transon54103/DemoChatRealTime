using System.ComponentModel.DataAnnotations;

namespace DemoChatRealTime.Models.Entities;

/// <summary>
/// NOTE - ChatRoom:
/// - H? tr? c? chat 1-1 (IsGroupChat = false) và nhóm (IsGroupChat = true).
/// - CreatedByUserId là nullable (int?) ? system-created rooms dùng NULL?
///
/// NOTE - Nullable FK Pattern:
///   Khi FK có th? không t?n t?i (system-created, anonymous, deleted user),
///   dùng int? thay vì fake value (0). EF Core hi?u int? = optional relationship.
/// </summary>
public class ChatRoom
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    public bool IsGroupChat { get; set; } = true;

    // NOTE: int? (nullable) ? system rooms có CreatedByUserId = null
    // int (non-nullable) s? t?o REQUIRED FK ? INSERT v?i 0 báo l?i vì User Id=0 không t?n t?i
    public int? CreatedByUserId { get; set; }
    public AppUser? CreatedByUser { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
    public ICollection<ChatRoomMember> Members { get; set; } = new List<ChatRoomMember>();
}
