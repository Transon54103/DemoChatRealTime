using System.Security.Claims;
using DemoChatRealTime.Models.DTOs;
using DemoChatRealTime.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace DemoChatRealTime.Hubs;

/// <summary>
/// NOTE - SignalR ChatHub:
/// - Hub là trung tâm real-time communication. Client g?i method trên Hub, Hub broadcast xu?ng clients.
/// - [Authorize] ??m b?o ch? user ?ã ??ng nh?p m?i connect ???c.
/// - M?i client có 1 ConnectionId unique, dùng ?? track và g?i message targeted.
///
/// FLOW REAL-TIME:
/// 1. Client connect ? OnConnectedAsync ? join SignalR groups (1 group = 1 room)
/// 2. Client g?i SendMessage ? Hub l?u DB ? broadcast t?i group
/// 3. Client disconnect ? OnDisconnectedAsync ? cleanup
///
/// QUAN TR?NG cho h? th?ng khác:
/// 1. SignalR Groups = logical grouping. Client join group ? nh?n message c?a group ?ó.
///    - Dùng cho: chat rooms, notifications, live updates...
/// 2. N?u multi-server: c?n SignalR Backplane (Redis, Azure SignalR Service, SQL Server)
///    - AddSignalR().AddStackExchangeRedis(...) ho?c AddAzureSignalR(...)
/// 3. Reconnection: SignalR JS client có auto-reconnect. C?n handle ? server side.
/// 4. Scale: m?i connection t?n ~1KB RAM. 10K users = ~10MB. Nh?ng c?n monitor.
/// 5. Message size limit: m?c ??nh 32KB. Tune n?u c?n g?i file/image.
/// 6. Backpressure: n?u client ch?m, messages queue up ? OOM. C?n monitor.
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
    /// NOTE: Khi client connect, t? ??ng join t?t c? rooms mà user là member.
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

        // Join t?t c? rooms mà user là member
        var rooms = await _chatService.GetUserRoomsAsync(userId.Value);
        foreach (var room in rooms)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"room_{room.Id}");
        }

        // Notify t?t c? clients r?ng user ?ã online
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
    /// NOTE: Client g?i method này ?? g?i message.
    /// Flow: Validate ? Save DB ? Broadcast to room group
    /// </summary>
    public async Task SendMessage(SendMessageDto dto)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return;

        var messageDto = await _chatService.SendMessageAsync(userId.Value, dto);
        if (messageDto == null)
        {
            // NOTE: G?i error v? cho caller n?u không g?i ???c
            await Clients.Caller.SendAsync("MessageError", "Không th? g?i tin nh?n. B?n có th? ch?a join room này.");
            return;
        }

        // NOTE: Broadcast message t?i t?t c? members trong room
        // Clients.Group() g?i t?i t?t c? connections ?ã join group ?ó
        await Clients.Group($"room_{dto.ChatRoomId}").SendAsync("ReceiveMessage", messageDto);
    }

    /// <summary>
    /// NOTE: Typing indicator - broadcast "user ?ang gõ" cho room.
    /// OthersInGroup = t?t c? trong group TR? caller (không c?n th?y mình ?ang gõ).
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
    /// NOTE: Join room m?i trong runtime.
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
    /// NOTE: L?y UserId t? Claims (Cookie Authentication).
    /// Claims ???c set khi login ? AuthController.
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
