using System.Security.Claims;
using DemoChatRealTime.Models.DTOs;
using DemoChatRealTime.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DemoChatRealTime.Controllers;

/// <summary>
/// NOTE - Chat Controller:
/// - [Authorize] ? class level ? t?t c? actions ??u c?n login.
/// - K?t h?p MVC Views (cho trang chat) + JSON API (cho AJAX calls t? JS).
/// - Trong production có th? tách: MVC controller cho views, API controller cho data.
///   Ho?c dùng Minimal API cho lightweight endpoints.
///
/// NOTE - AntiForgery cho AJAX:
/// - MVC form có @Html.AntiForgeryToken() t? ??ng.
/// - AJAX POST c?n g?i token qua header: RequestVerificationToken
/// - Ho?c dùng [IgnoreAntiforgeryToken] cho JSON API endpoints (b?o v? b?i Cookie Auth + SameSite).
/// - Trong demo này, các JSON API endpoints không dùng AntiForgery vì:
///   + Cookie SameSite=Lax ?ã ch?ng CSRF c? b?n
///   + SignalR Hub c?ng không có AntiForgery
///   + Production nên ?ánh giá thêm tùy threat model
/// </summary>
[Authorize]
public class ChatController : Controller
{
    private readonly IChatService _chatService;

    public ChatController(IChatService chatService)
    {
        _chatService = chatService;
    }

    /// <summary>
    /// Trang chính chat - load rooms và render view
    /// </summary>
    public async Task<IActionResult> Index()
    {
        var userId = GetCurrentUserId();
        var rooms = await _chatService.GetUserRoomsAsync(userId);
        return View(rooms);
    }

    /// <summary>
    /// API: L?y danh sách messages c?a room (v?i pagination)
    /// NOTE: Dùng [HttpGet] + JSON response cho AJAX calls
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetMessages(int roomId, int beforeId = 0, int pageSize = 50)
    {
        var messages = await _chatService.GetRoomMessagesAsync(roomId, pageSize, beforeId);
        return Json(messages);
    }

    /// <summary>
    /// API: L?y thông tin room
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetRoomInfo(int roomId)
    {
        var room = await _chatService.GetRoomInfoAsync(roomId);
        if (room == null) return NotFound();
        return Json(room);
    }

    /// <summary>
    /// API: T?o room m?i
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateRoom([FromBody] CreateRoomDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var userId = GetCurrentUserId();
        var room = await _chatService.CreateRoomAsync(dto.Name, userId);

        if (room == null) return BadRequest("Không th? t?o phòng");

        return Json(new ChatRoomDto
        {
            Id = room.Id,
            Name = room.Name,
            IsGroupChat = room.IsGroupChat,
            MemberCount = 1
        });
    }

    /// <summary>
    /// API: L?y danh sách rooms c?a user hi?n t?i
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetMyRooms()
    {
        var userId = GetCurrentUserId();
        var rooms = await _chatService.GetUserRoomsAsync(userId);
        return Json(rooms);
    }

    /// <summary>
    /// API: L?y danh sách rooms mà user CH?A join (?? browse + join)
    /// NOTE: Tách riêng v?i GetMyRooms ?? rõ ràng semantics
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAvailableRooms()
    {
        var userId = GetCurrentUserId();
        var rooms = await _chatService.GetAvailableRoomsAsync(userId);
        return Json(rooms);
    }

    /// <summary>
    /// API: L?y online users
    /// </summary>
    [HttpGet]
    public IActionResult GetOnlineUsers()
    {
        var users = _chatService.GetOnlineUsers();
        return Json(users);
    }

    #region Helpers

    private int GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.Parse(claim!);
    }

    #endregion
}
