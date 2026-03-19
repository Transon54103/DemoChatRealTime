using DemoChatRealTime.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace DemoChatRealTime.Data;

/// <summary>
/// NOTE - DbContext:
/// - ?ây là n?i c?u hình mapping Entity ? Table trong MSSQL.
/// - Dùng Fluent API trong OnModelCreating ?? c?u hình relationship rõ ràng h?n Data Annotations.
/// - Index trên Username (unique) và ChatRoomId+SentAt (query performance).
/// - Trong production:
///   + Tách DbContext n?u h? th?ng l?n (Bounded Context - DDD).
///   + Dùng Migration strategy phù h?p (EF Migrations cho dev, SQL scripts cho production).
///   + Cân nh?c Read/Write splitting n?u traffic cao.
/// </summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<AppUser> Users { get; set; }
    public DbSet<ChatRoom> ChatRooms { get; set; }
    public DbSet<ChatMessage> ChatMessages { get; set; }
    public DbSet<ChatRoomMember> ChatRoomMembers { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ===== User =====
        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.ToTable("Users");

            // NOTE: Unique index trên Username - không cho phép trùng
            entity.HasIndex(u => u.Username).IsUnique();
        });

        // ===== ChatRoom =====
        modelBuilder.Entity<ChatRoom>(entity =>
        {
            entity.ToTable("ChatRooms");

            // NOTE: CreatedByUserId là int? (nullable) ? optional FK
            // System-created rooms (nh? "General") có CreatedByUserId = null
            // EF Core t? hi?u int? = optional relationship, nh?ng explicit config cho rõ ràng
            entity.HasOne(r => r.CreatedByUser)
                  .WithMany()
                  .HasForeignKey(r => r.CreatedByUserId)
                  .IsRequired(false)                     // Optional relationship
                  .OnDelete(DeleteBehavior.Restrict);    // Không cascade xóa user ? xóa rooms
        });

        // ===== ChatMessage =====
        modelBuilder.Entity<ChatMessage>(entity =>
        {
            entity.ToTable("ChatMessages");

            // NOTE: Composite Index cho vi?c query messages theo room + th?i gian
            // R?t quan tr?ng cho performance khi load chat history
            entity.HasIndex(m => new { m.ChatRoomId, m.SentAt });

            entity.HasOne(m => m.Sender)
                  .WithMany(u => u.Messages)
                  .HasForeignKey(m => m.SenderId)
                  .OnDelete(DeleteBehavior.Restrict); // NOTE: Không cascade - xóa user không xóa messages

            entity.HasOne(m => m.ChatRoom)
                  .WithMany(r => r.Messages)
                  .HasForeignKey(m => m.ChatRoomId)
                  .OnDelete(DeleteBehavior.Restrict); // NOTE: Restrict thay Cascade - tránh multi-cascade path
                  // Xóa room ph?i xóa messages tr??c (application code x? lý)
                  // Production nên dùng soft delete, không hard delete
        });

        // ===== ChatRoomMember (Many-to-Many) =====
        modelBuilder.Entity<ChatRoomMember>(entity =>
        {
            entity.ToTable("ChatRoomMembers");

            // NOTE: Unique constraint - 1 user ch? join 1 room 1 l?n
            entity.HasIndex(m => new { m.UserId, m.ChatRoomId }).IsUnique();

            // NOTE: Dùng Restrict (NO ACTION) thay vì Cascade cho C? HAI FK
            // Lý do: SQL Server không cho phép multiple cascade paths t?i cùng 1 table.
            //
            // ? ?ây ChatRoomMember có 2 FK: UserId ? Users, ChatRoomId ? ChatRooms
            // N?u c? 2 ??u Cascade:
            //   Path 1: Xóa User ? cascade xóa ChatRoomMember
            //   Path 2: Xóa ChatRoom ? cascade xóa ChatRoomMember
            //   ? SQL Server báo l?i "may cause cycles or multiple cascade paths"
            //
            // Gi?i pháp:
            //   - Restrict = DB t? ch?i xóa User/Room n?u còn members
            //   - Application code ph?i xóa members tr??c khi xóa User/Room
            //   - ?ây là best practice vì tránh xóa nh?m data quan tr?ng (defense-in-depth)
            //
            // Alternatives trong production:
            //   1. Soft delete (IsDeleted flag) ? không bao gi? hard delete
            //   2. Database trigger ?? cleanup
            //   3. Ch? 1 FK cascade, FK còn l?i restrict
            entity.HasOne(m => m.User)
                  .WithMany(u => u.ChatRoomMembers)
                  .HasForeignKey(m => m.UserId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(m => m.ChatRoom)
                  .WithMany(r => r.Members)
                  .HasForeignKey(m => m.ChatRoomId)
                  .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
