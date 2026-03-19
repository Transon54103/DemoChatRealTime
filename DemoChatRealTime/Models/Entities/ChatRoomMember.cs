using System.ComponentModel.DataAnnotations;

namespace DemoChatRealTime.Models.Entities;

/// <summary>
/// NOTE - ChatRoomMember (Many-to-Many):
/// - ?ây là b?ng trung gian gi?a User và ChatRoom.
/// - JoinedAt giúp bi?t user join room khi nào ? ch? load message sau th?i ?i?m join.
/// - Role có th? dùng ?? phân quy?n: Admin, Member, Moderator...
/// - Trong production nên thêm:
///   + LastReadMessageId - ?ánh d?u tin nh?n cu?i user ?ã ??c ? tính unread count
///   + IsMuted - t?t notification cho room c? th?
///   + LeftAt - cho phép r?i room mà v?n gi? l?ch s?
/// </summary>
public class ChatRoomMember
{
    [Key]
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser? User { get; set; }

    public int ChatRoomId { get; set; }
    public ChatRoom? ChatRoom { get; set; }

    [MaxLength(20)]
    public string Role { get; set; } = "Member"; // "Admin", "Member"

    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}
