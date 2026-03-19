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
            // Xóa DB c? r?i t?o l?i v?i schema m?i.
            // Production KHÔNG BAO GI? dùng cách này — dùng Migrations.
            //
            // N?u b?n ?ã có data quan tr?ng, comment dòng EnsureDeleted
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

        // Seed default "General" room n?u ch?a có
        if (!await context.ChatRooms.AnyAsync())
        {
            context.ChatRooms.Add(new ChatRoom
            {
                Name = "General",
                IsGroupChat = true,
                CreatedByUserId = null, // NOTE: null (không ph?i 0) ? không vi ph?m FK
                CreatedAt = DateTime.UtcNow
            });
            await context.SaveChangesAsync();
            logger.LogInformation("Seeded default 'General' room");
        }
    }
}
