using DemoChatRealTime.Data;
using DemoChatRealTime.Models.DTOs;
using DemoChatRealTime.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace DemoChatRealTime.Services;

/// <summary>
/// NOTE - ChatService:
/// - T?ng Service ch?a business logic, Controller ch? ?i?u ph?i.
/// - Cache strategy: Cache-Aside pattern
///   + Read: check cache ? miss ? query DB ? set cache
///   + Write: update DB ? invalidate cache
/// 
/// QUAN TR?NG cho h? th?ng khác:
/// 1. IMemoryCache ch? ho?t ??ng single-server. Multi-server dùng Redis (IDistributedCache).
/// 2. Cache invalidation là bài toán khó nh?t trong CS - c?n strategy rõ ràng.
/// 3. TTL (Time To Live) c?n tune theo use case:
///    - Chat messages: TTL ng?n (5 phút) vì data thay ??i liên t?c
///    - Room list: TTL v?a (15-30 phút)
///    - User profile: TTL dài (1 gi?)
/// 4. Pagination cho messages - KHÔNG load h?t (offset ho?c cursor-based).
/// 5. Trong production: dùng CQRS pattern tách read/write model n?u traffic cao.
/// </summary>
public interface IChatService
{
    // Room operations
    Task<List<ChatRoomDto>> GetUserRoomsAsync(int userId);
    Task<List<ChatRoomDto>> GetAvailableRoomsAsync(int userId);
    Task<ChatRoom?> CreateRoomAsync(string name, int createdByUserId);
    Task<bool> JoinRoomAsync(int userId, int roomId);
    Task<ChatRoomDto?> GetRoomInfoAsync(int roomId);

    // Message operations
    Task<ChatMessageDto?> SendMessageAsync(int senderId, SendMessageDto dto);
    Task<List<ChatMessageDto>> GetRoomMessagesAsync(int roomId, int pageSize = 50, int beforeId = 0);

    // Online tracking
    void SetUserOnline(int userId, string connectionId);
    void SetUserOffline(string connectionId);
    List<UserOnlineDto> GetOnlineUsers();
    int? GetUserIdByConnectionId(string connectionId);
    string? GetConnectionIdByUserId(int userId);
}

public class ChatService : IChatService
{
    private readonly AppDbContext _context;
    private readonly IMemoryCache _cache;
    private readonly ILogger<ChatService> _logger;

    /// <summary>
    /// NOTE: Cache key constants - ??t tên rõ ràng, có prefix ?? tránh conflict.
    /// Pattern: "{domain}:{entity}:{identifier}"
    /// </summary>
    private const string CACHE_ROOMS_PREFIX = "chat:rooms:user:";
    private const string CACHE_MESSAGES_PREFIX = "chat:messages:room:";
    private const string CACHE_ONLINE_USERS = "chat:online_users";

    // NOTE: In-memory tracking cho online users
    // Production nên dùng Redis sorted set ho?c presence channel
    private static readonly Dictionary<string, int> _connectionUserMap = new(); // connectionId ? userId
    private static readonly Dictionary<int, string> _userConnectionMap = new(); // userId ? connectionId
    private static readonly object _lockObj = new();

    public ChatService(AppDbContext context, IMemoryCache cache, ILogger<ChatService> logger)
    {
        _context = context;
        _cache = cache;
        _logger = logger;
    }

    #region Room Operations

    public async Task<List<ChatRoomDto>> GetUserRoomsAsync(int userId)
    {
        var cacheKey = $"{CACHE_ROOMS_PREFIX}{userId}";

        // NOTE: Cache-Aside Pattern: check cache tr??c, miss thì query DB
        if (_cache.TryGetValue(cacheKey, out List<ChatRoomDto>? cachedRooms) && cachedRooms != null)
        {
            _logger.LogDebug("Cache HIT for user rooms: {UserId}", userId);
            return cachedRooms;
        }

        _logger.LogDebug("Cache MISS for user rooms: {UserId}", userId);

        var rooms = await _context.ChatRoomMembers
            .Where(m => m.UserId == userId)
            .Select(m => m.ChatRoom!)
            .Select(r => new ChatRoomDto
            {
                Id = r.Id,
                Name = r.Name,
                IsGroupChat = r.IsGroupChat,
                MemberCount = r.Members.Count,
                LastMessage = r.Messages
                    .OrderByDescending(msg => msg.SentAt)
                    .Select(msg => msg.Content)
                    .FirstOrDefault(),
                LastMessageAt = r.Messages
                    .OrderByDescending(msg => msg.SentAt)
                    .Select(msg => (DateTime?)msg.SentAt)
                    .FirstOrDefault()
            })
            .OrderByDescending(r => r.LastMessageAt)
            .ToListAsync();

        // NOTE: Set cache v?i TTL 5 phút.
        // SlidingExpiration: reset TTL m?i l?n truy c?p (user active thì cache s?ng lâu h?n)
        // AbsoluteExpirationRelativeToNow: TTL tuy?t ??i, dù truy c?p hay không c?ng h?t h?n
        var cacheOptions = new MemoryCacheEntryOptions()
            .SetSlidingExpiration(TimeSpan.FromMinutes(2))
            .SetAbsoluteExpiration(TimeSpan.FromMinutes(10));

        _cache.Set(cacheKey, rooms, cacheOptions);
        return rooms;
    }

    /// <summary>
    /// NOTE - Browse Available Rooms:
    /// - Tr? v? các room mà user CH?A join ? cho phép khám phá + join.
    /// - Trong production:
    ///   + Thêm search/filter (theo tên, tag, category)
    ///   + Pagination cho room list n?u s? l??ng l?n
    ///   + Private rooms c?n invite link, không hi?n trong browse
    /// </summary>
    public async Task<List<ChatRoomDto>> GetAvailableRoomsAsync(int userId)
    {
        var joinedRoomIds = await _context.ChatRoomMembers
            .Where(m => m.UserId == userId)
            .Select(m => m.ChatRoomId)
            .ToListAsync();

        var rooms = await _context.ChatRooms
            .Where(r => !joinedRoomIds.Contains(r.Id))
            .Select(r => new ChatRoomDto
            {
                Id = r.Id,
                Name = r.Name,
                IsGroupChat = r.IsGroupChat,
                MemberCount = r.Members.Count
            })
            .OrderBy(r => r.Name)
            .ToListAsync();

        return rooms;
    }

    public async Task<ChatRoom?> CreateRoomAsync(string name, int createdByUserId)
    {
        var room = new ChatRoom
        {
            Name = name,
            IsGroupChat = true,
            CreatedByUserId = createdByUserId,
            CreatedAt = DateTime.UtcNow
        };

        _context.ChatRooms.Add(room);
        await _context.SaveChangesAsync();

        // Auto-join creator
        _context.ChatRoomMembers.Add(new ChatRoomMember
        {
            UserId = createdByUserId,
            ChatRoomId = room.Id,
            Role = "Admin",
            JoinedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        // NOTE: Invalidate cache khi data thay ??i
        InvalidateUserRoomsCache(createdByUserId);

        _logger.LogInformation("Room {RoomName} created by user {UserId}", name, createdByUserId);
        return room;
    }

    public async Task<bool> JoinRoomAsync(int userId, int roomId)
    {
        // Check ?ã join ch?a
        var exists = await _context.ChatRoomMembers
            .AnyAsync(m => m.UserId == userId && m.ChatRoomId == roomId);

        if (exists) return false;

        _context.ChatRoomMembers.Add(new ChatRoomMember
        {
            UserId = userId,
            ChatRoomId = roomId,
            Role = "Member",
            JoinedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        // Invalidate cache
        InvalidateUserRoomsCache(userId);

        return true;
    }

    public async Task<ChatRoomDto?> GetRoomInfoAsync(int roomId)
    {
        return await _context.ChatRooms
            .Where(r => r.Id == roomId)
            .Select(r => new ChatRoomDto
            {
                Id = r.Id,
                Name = r.Name,
                IsGroupChat = r.IsGroupChat,
                MemberCount = r.Members.Count
            })
            .FirstOrDefaultAsync();
    }

    #endregion

    #region Message Operations

    public async Task<ChatMessageDto?> SendMessageAsync(int senderId, SendMessageDto dto)
    {
        // Verify user is member of room
        var isMember = await _context.ChatRoomMembers
            .AnyAsync(m => m.UserId == senderId && m.ChatRoomId == dto.ChatRoomId);

        if (!isMember)
        {
            _logger.LogWarning("User {UserId} tried to send message to room {RoomId} without being a member",
                senderId, dto.ChatRoomId);
            return null;
        }

        var sender = await _context.Users.FindAsync(senderId);
        if (sender == null) return null;

        var message = new ChatMessage
        {
            Content = dto.Content,
            MessageType = "Text",
            SenderId = senderId,
            ChatRoomId = dto.ChatRoomId,
            SentAt = DateTime.UtcNow
        };

        _context.ChatMessages.Add(message);
        await _context.SaveChangesAsync();

        // NOTE: Invalidate message cache cho room + room list cache cho t?t c? members
        InvalidateRoomMessagesCache(dto.ChatRoomId);
        await InvalidateAllMembersRoomsCacheAsync(dto.ChatRoomId);

        return new ChatMessageDto
        {
            Id = message.Id,
            Content = message.Content,
            MessageType = message.MessageType,
            SentAt = message.SentAt,
            SenderId = senderId,
            SenderName = sender.DisplayName,
            ChatRoomId = dto.ChatRoomId
        };
    }

    /// <summary>
    /// NOTE - Cursor-based Pagination:
    /// - Dùng "beforeId" thay vì offset (page number).
    /// - T?i sao? Vì offset pagination có v?n ?? khi data thêm m?i liên t?c:
    ///   + Page 1 load xong, có message m?i ? page 2 s? b? duplicate message cu?i page 1
    /// - Cursor-based: "L?y N messages có ID nh? h?n X" ? luôn chính xác.
    /// - R?t phù h?p cho chat, feed, timeline...
    /// </summary>
    public async Task<List<ChatMessageDto>> GetRoomMessagesAsync(int roomId, int pageSize = 50, int beforeId = 0)
    {
        var cacheKey = $"{CACHE_MESSAGES_PREFIX}{roomId}:before:{beforeId}:size:{pageSize}";

        if (_cache.TryGetValue(cacheKey, out List<ChatMessageDto>? cachedMessages) && cachedMessages != null)
        {
            return cachedMessages;
        }

        var query = _context.ChatMessages
            .Where(m => m.ChatRoomId == roomId);

        if (beforeId > 0)
        {
            query = query.Where(m => m.Id < beforeId);
        }

        var messages = await query
            .OrderByDescending(m => m.SentAt)
            .Take(pageSize)
            .Select(m => new ChatMessageDto
            {
                Id = m.Id,
                Content = m.Content,
                MessageType = m.MessageType,
                SentAt = m.SentAt,
                SenderId = m.SenderId,
                SenderName = m.Sender!.DisplayName,
                ChatRoomId = m.ChatRoomId
            })
            .ToListAsync();

        // Reverse ?? hi?n th? chronological order (c? ? m?i)
        messages.Reverse();

        var cacheOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(TimeSpan.FromMinutes(2));

        _cache.Set(cacheKey, messages, cacheOptions);
        return messages;
    }

    #endregion

    #region Online Tracking

    /// <summary>
    /// NOTE - Online User Tracking:
    /// - Dùng static Dictionary cho demo (single server).
    /// - Lock ?? thread-safe (SignalR hub có th? g?i ??ng th?i t? nhi?u connection).
    /// - Production dùng Redis:
    ///   + HSET online_users {userId} {connectionId}
    ///   + HDEL online_users {userId}
    ///   + TTL + heartbeat ?? t? cleanup n?u client disconnect b?t th??ng
    ///   + Redis Pub/Sub ?? sync state gi?a multiple servers
    /// </summary>
    public void SetUserOnline(int userId, string connectionId)
    {
        lock (_lockObj)
        {
            // Remove old connection if exists
            if (_userConnectionMap.TryGetValue(userId, out var oldConn))
            {
                _connectionUserMap.Remove(oldConn);
            }

            _connectionUserMap[connectionId] = userId;
            _userConnectionMap[userId] = connectionId;
        }

        _cache.Remove(CACHE_ONLINE_USERS); // Invalidate online users cache
        _logger.LogInformation("User {UserId} online with connection {ConnectionId}", userId, connectionId);
    }

    public void SetUserOffline(string connectionId)
    {
        lock (_lockObj)
        {
            if (_connectionUserMap.TryGetValue(connectionId, out var userId))
            {
                _connectionUserMap.Remove(connectionId);
                _userConnectionMap.Remove(userId);
                _logger.LogInformation("User {UserId} offline", userId);
            }
        }

        _cache.Remove(CACHE_ONLINE_USERS);
    }

    public List<UserOnlineDto> GetOnlineUsers()
    {
        lock (_lockObj)
        {
            return _userConnectionMap.Select(kv => new UserOnlineDto
            {
                UserId = kv.Key,
                IsOnline = true
            }).ToList();
        }
    }

    public int? GetUserIdByConnectionId(string connectionId)
    {
        lock (_lockObj)
        {
            return _connectionUserMap.TryGetValue(connectionId, out var userId) ? userId : null;
        }
    }

    public string? GetConnectionIdByUserId(int userId)
    {
        lock (_lockObj)
        {
            return _userConnectionMap.TryGetValue(userId, out var connId) ? connId : null;
        }
    }

    #endregion

    #region Cache Helpers

    /// <summary>
    /// NOTE: Cache Invalidation Strategy
    /// - Khi send message ? invalidate room messages cache + room list cache cho t?t c? members
    /// - ?ây là "Write-through invalidation" pattern
    /// - ??n gi?n nh?ng hi?u qu? cho h?u h?t use cases
    /// </summary>
    private void InvalidateUserRoomsCache(int userId)
    {
        _cache.Remove($"{CACHE_ROOMS_PREFIX}{userId}");
    }

    private void InvalidateRoomMessagesCache(int roomId)
    {
        // NOTE: Ch? invalidate latest messages (beforeId=0)
        // Các page c? h?n ít thay ??i nên ?? t? expire
        _cache.Remove($"{CACHE_MESSAGES_PREFIX}{roomId}:before:0:size:50");
    }

    private async Task InvalidateAllMembersRoomsCacheAsync(int roomId)
    {
        var memberIds = await _context.ChatRoomMembers
            .Where(m => m.ChatRoomId == roomId)
            .Select(m => m.UserId)
            .ToListAsync();

        foreach (var memberId in memberIds)
        {
            InvalidateUserRoomsCache(memberId);
        }
    }

    #endregion
}
