using System.Security.Claims;
using DemoChatRealTime.Models.DTOs;
using DemoChatRealTime.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace DemoChatRealTime.Hubs;

/// <summary>
/// NOTE - SignalR ChatHub:
/// - Hub là trung tâm real-time communication. Client gửi method trên Hub, Hub broadcast xuống clients.
/// - [Authorize] đảm bảo chỉ user đã đăng nhập mới connect được.
/// - Mỗi client có 1 ConnectionId unique, dùng để track và gửi message targeted.
///
/// FLOW REAL-TIME:
/// 1. Client connect ? OnConnectedAsync ? join SignalR groups (1 group = 1 room)
/// 2. Client gửi SendMessage ? Hub lưu DB ? broadcast tới group
/// 3. Client disconnect ? OnDisconnectedAsync ? cleanup
///
/// QUAN TRỌNG cho hệ thống khác:
/// 1. SignalR Groups = logical grouping. Client join group ? nhận message của group đó.
///    - Dùng cho: chat rooms, notifications, live updates...
/// 2. Nếu multi-server: cần SignalR Backplane (Redis, Azure SignalR Service, SQL Server)
///    - AddSignalR().AddStackExchangeRedis(...) hoặc AddAzureSignalR(...)
/// 3. Reconnection: SignalR JS client có auto-reconnect. Cần handle ở server side.
/// 4. Scale: mỗi connection tốn ~1KB RAM. 10K users = ~10MB. Nhưng cần monitor.
/// 5. Message size limit: mặc định 32KB. Tune nếu cần gửi file/image.
/// 6. Backpressure: nếu client chậm, messages queue up -> OOM. Cần monitor.
/// </summary>
[Authorize]
public class ChatHub : Hub
{
    private readonly IChatService _chatService;
    private readonly IAuthService _authService;
    private readonly ILogger<ChatHub> _logger;

    public ChatHub(IChatService chatService, IAuthService authService, ILogger<ChatHub> logger)
    {
        _chatService = chatService;
        _authService = authService;
        _logger = logger;
    }

    /// <summary>
    /// NOTE: Khi client connect, tự động join tất cả rooms mà user là member.
    /// SignalR Group name = "room_{roomId}"
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        var userId = GetCurrentUserId();
        if (userId == null)
        {
            Context.Abort();
            return;
        }

        // Track online status
        _chatService.SetUserOnline(userId.Value, Context.ConnectionId);

        // Join tất cả rooms mà user là member
        var rooms = await _chatService.GetUserRoomsAsync(userId.Value);
        foreach (var room in rooms)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"room_{room.Id}");
        }

        // Notify tất cả clients rằng user đã online
        var user = await _authService.GetUserByIdAsync(userId.Value);
        await Clients.All.SendAsync("UserOnline", new UserOnlineDto
        {
            UserId = userId.Value,
            DisplayName = user?.DisplayName ?? "Unknown",
            IsOnline = true
        });

        _logger.LogInformation("User {UserId} connected to ChatHub", userId.Value);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = _chatService.GetUserIdByConnectionId(Context.ConnectionId);

        _chatService.SetUserOffline(Context.ConnectionId);

        if (userId.HasValue)
        {
            var user = await _authService.GetUserByIdAsync(userId.Value);
            await Clients.All.SendAsync("UserOffline", new UserOnlineDto
            {
                UserId = userId.Value,
                DisplayName = user?.DisplayName ?? "Unknown",
                IsOnline = false
            });
        }

        _logger.LogInformation("User disconnected from ChatHub. ConnectionId: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// NOTE: Client gửi method này để gửi message.
    /// Flow: Validate -> Save DB -> Broadcast to room group
    /// </summary>
    public async Task SendMessage(SendMessageDto dto)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return;

        var messageDto = await _chatService.SendMessageAsync(userId.Value, dto);
        if (messageDto == null)
        {
            // NOTE: Gửi error về cho caller nếu không gởi được
            await Clients.Caller.SendAsync("MessageError", "Không thể gởi tin nhắn. Bạn có thể chưa join room này.");
            return;
        }

        // NOTE: Broadcast message tới tất cả members trong room
        // Clients.Group() gửi tới tất cả connections đã join group đó
        await Clients.Group($"room_{dto.ChatRoomId}").SendAsync("ReceiveMessage", messageDto);
    }

    /// <summary>
    /// NOTE: Typing indicator - broadcast "user đang gõ" cho room.
    /// OthersInGroup = tất cả trong group TRỪ caller (không cần thấy mình đang gõ).
    /// </summary>
    public async Task StartTyping(int roomId)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return;

        var user = await _authService.GetUserByIdAsync(userId.Value);
        await Clients.OthersInGroup($"room_{roomId}").SendAsync("UserTyping", new
        {
            UserId = userId.Value,
            DisplayName = user?.DisplayName ?? "Unknown",
            RoomId = roomId
        });
    }

    public async Task StopTyping(int roomId)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return;

        await Clients.OthersInGroup($"room_{roomId}").SendAsync("UserStoppedTyping", new
        {
            UserId = userId.Value,
            RoomId = roomId
        });
    }

    /// <summary>
    /// NOTE: Join room mới trong runtime.
    /// Sau khi join DB, add connection vào SignalR group.
    /// </summary>
    public async Task JoinRoom(int roomId)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return;

        var success = await _chatService.JoinRoomAsync(userId.Value, roomId);
        if (success)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"room_{roomId}");

            var user = await _authService.GetUserByIdAsync(userId.Value);
            // Notify room members
            await Clients.Group($"room_{roomId}").SendAsync("UserJoinedRoom", new
            {
                UserId = userId.Value,
                DisplayName = user?.DisplayName ?? "Unknown",
                RoomId = roomId
            });

            // Notify caller
            await Clients.Caller.SendAsync("JoinedRoom", roomId);
        }
    }

    #region Helpers

    /// <summary>
    /// NOTE: Lấy UserId từ Claims (Cookie Authentication).
    /// Claims được set khi login ở AuthController.
    /// </summary>
    private int? GetCurrentUserId()
    {
        var userIdClaim = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (int.TryParse(userIdClaim, out var userId))
        {
            return userId;
        }
        return null;
    }

    #endregion
}
