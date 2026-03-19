using System.ComponentModel.DataAnnotations;

namespace DemoChatRealTime.Models.Entities;

/// <summary>
/// NOTE - ChatMessage:
/// - SentAt l?u UTC ?? tránh v?n ?? timezone. Client t? convert sang local time.
/// - MessageType ?? m? r?ng: text, image, file, system notification...
/// - Trong production nên thêm:
///   + IsDeleted (soft delete) - cho phép xóa tin nh?n
///   + EditedAt - cho phép ch?nh s?a tin nh?n
///   + ReplyToMessageId - reply thread
///   + Reactions (b?ng riêng) - emoji reactions
///   + ReadReceipts (b?ng riêng) - ?ánh d?u ?ã ??c
/// </summary>
public class ChatMessage
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(2000)]
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// NOTE: "Text", "Image", "File", "System"
    /// Dùng enum ho?c const string. Demo dùng string cho ??n gi?n.
    /// </summary>
    [MaxLength(20)]
    public string MessageType { get; set; } = "Text";

    public DateTime SentAt { get; set; } = DateTime.UtcNow;

    // Foreign keys
    public int SenderId { get; set; }
    public AppUser? Sender { get; set; }

    public int ChatRoomId { get; set; }
    public ChatRoom? ChatRoom { get; set; }
}
