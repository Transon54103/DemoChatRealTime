using DemoChatRealTime.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace DemoChatRealTime.Data;

/// <summary>
/// NOTE - Database Initializer:
/// - Dùng EnsureCreated() cho demo. Production dùng Migrations.
/// - Seed data ? ?ây thay vì trong OnModelCreating ?? tránh FK issues.
/// - Pattern: g?i trong Program.cs khi app start.
/// - Trong production nên dùng:
///   + DbContext.Database.MigrateAsync() thay cho EnsureCreated()
///   + Separate migration project n?u solution l?n
///   + Health check endpoint ?? verify DB connectivity
/// </summary>
public static class DbInitializer
{
    public static async Task InitializeAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<AppDbContext>>();

        try
        {
            // NOTE: EnsureDeleted + EnsureCreated cho DEV ONLY
            // Xóa DB cũ rồi tạo lại với schema mới.
            // Production KHÔNG BAO GIỜ dùng cách này — dùng Migrations.
            //
            // Nếu bạn đã có data quan trọng, comment dòng EnsureDeleted
            // và dùng: dotnet ef migrations add <name> + dotnet ef database update
            //await context.Database.EnsureDeletedAsync();
            await context.Database.EnsureCreatedAsync();

            logger.LogInformation("Database created successfully");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating database. Check connection string in appsettings.json");
            throw;
        }

        // Seed default "General" room nếu chưa có
        if (!await context.ChatRooms.AnyAsync())
        {
            context.ChatRooms.Add(new ChatRoom
            {
                Name = "General",
                IsGroupChat = true,
                CreatedByUserId = null, // NOTE: null (không phải 0) để không vi phạm FK
                CreatedAt = DateTime.UtcNow
            });
            await context.SaveChangesAsync();
            logger.LogInformation("Seeded default 'General' room");
        }
    }
}
