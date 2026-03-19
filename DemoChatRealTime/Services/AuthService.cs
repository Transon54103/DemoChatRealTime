using System.Security.Cryptography;
using System.Text;
using DemoChatRealTime.Data;
using DemoChatRealTime.Models.DTOs;
using DemoChatRealTime.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace DemoChatRealTime.Services;

/// <summary>
/// NOTE - AuthService:
/// - Tách business logic ra kh?i Controller ? d? test, d? maintain.
/// - Dùng HMACSHA512 cho demo. Production nên dùng BCrypt/Argon2 (ch?ng brute-force t?t h?n).
/// - Interface ? Implementation pattern cho DI ? d? mock trong unit test.
/// 
/// QUAN TR?NG cho h? th?ng khác:
/// 1. Luôn hash password, KHÔNG BAO GI? l?u plain text
/// 2. Salt unique cho m?i user ? cùng password nh?ng hash khác nhau
/// 3. Trong production dùng ASP.NET Identity ho?c external provider (Google, Azure AD...)
/// 4. Rate limiting cho login endpoint (ch?ng brute-force)
/// 5. Account lockout sau N l?n sai password
/// </summary>
public interface IAuthService
{
    Task<(bool Success, string Message, AppUser? User)> RegisterAsync(RegisterDto dto);
    Task<(bool Success, string Message, AppUser? User)> LoginAsync(LoginDto dto);
    Task<AppUser?> GetUserByIdAsync(int userId);
}

public class AuthService : IAuthService
{
    private readonly AppDbContext _context;
    private readonly ILogger<AuthService> _logger;

    public AuthService(AppDbContext context, ILogger<AuthService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<(bool Success, string Message, AppUser? User)> RegisterAsync(RegisterDto dto)
    {
        // Check username ?ã t?n t?i ch?a
        if (await _context.Users.AnyAsync(u => u.Username == dto.Username.ToLower()))
        {
            return (false, "Username ?ã t?n t?i", null);
        }

        // NOTE: HMACSHA512 t?o key (salt) random + compute hash
        using var hmac = new HMACSHA512();

        var user = new AppUser
        {
            Username = dto.Username.ToLower(),
            DisplayName = dto.DisplayName,
            PasswordHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(dto.Password)),
            PasswordSalt = hmac.Key, // NOTE: Key chính là Salt, l?u l?i ?? verify sau
            CreatedAt = DateTime.UtcNow
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        // Auto-join room "General"
        var generalRoom = await _context.ChatRooms.FirstOrDefaultAsync(r => r.Name == "General");
        if (generalRoom != null)
        {
            _context.ChatRoomMembers.Add(new ChatRoomMember
            {
                UserId = user.Id,
                ChatRoomId = generalRoom.Id,
                Role = "Member",
                JoinedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
        }

        _logger.LogInformation("User {Username} registered successfully", user.Username);
        return (true, "??ng ký thành công", user);
    }

    public async Task<(bool Success, string Message, AppUser? User)> LoginAsync(LoginDto dto)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Username == dto.Username.ToLower());

        if (user == null)
        {
            return (false, "Username không t?n t?i", null);
        }

        // NOTE: Dùng l?i Salt (Key) c?a user ?? compute hash và so sánh
        using var hmac = new HMACSHA512(user.PasswordSalt);
        var computedHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(dto.Password));

        // So sánh t?ng byte - SequenceEqual an toàn h?n == cho byte arrays
        if (!computedHash.SequenceEqual(user.PasswordHash))
        {
            return (false, "Password không ?úng", null);
        }

        _logger.LogInformation("User {Username} logged in", user.Username);
        return (true, "??ng nh?p thành công", user);
    }

    public async Task<AppUser?> GetUserByIdAsync(int userId)
    {
        return await _context.Users.FindAsync(userId);
    }
}
