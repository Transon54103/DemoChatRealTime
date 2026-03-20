/**
 * chat-init.js
 * Entry point — khởi tạo ứng dụng chat.
 *
 * Nhiệm vụ:
 *  1. Khai báo ChatState (global state object duy nhất)
 *  2. Nhận server-side data được inject vào window.ChatConfig từ Razor View
 *  3. Khởi tạo SignalR
 *  4. Gắn event listeners lên các phần tử DOM
 *
 * Thứ tự load script (trong View):
 *  1. chat-utils.js
 *  2. chat-ui.js
 *  3. chat-signalr.js
 *  4. chat-rooms.js
 *  5. chat-init.js  ← file này
 *
 * Dữ liệu từ server (inject qua <script> trong View):
 *  window.ChatConfig = {
 *      currentUserId: int,
 *      currentDisplayName: string,
 *      rooms: [{ id, name }]   // để khởi tạo roomNameMap
 *  };
 */

// =====================================================
// GLOBAL STATE
// =====================================================

/**
 * ChatState — single source of truth cho toàn bộ JS chat.
 * Không dùng biến global rải rác, tất cả tập trung vào object này.
 */
const ChatState = {
    // User hiện tại (inject từ server)
    currentUserId: null,
    currentDisplayName: null,

    // Room đang mở
    currentRoomId: null,

    // SignalR connection instance
    connection: null,

    // Pagination: ID nhỏ nhất đã load (để load thêm về trước)
    oldestMessageId: 0,

    // Typing indicator debounce
    isTyping: false,
    typingTimeout: null,

    // { roomId: unreadCount }
    unreadCounts: {},

    // { roomId: roomName } — cache tên room để dùng cho toast
    roomNameMap: {}
};

// =====================================================
// KHỞI TẠO
// =====================================================

document.addEventListener('DOMContentLoaded', function () {
    // 1. Nạp config từ server (inject bởi Razor View)
    const config = window.ChatConfig;
    if (!config) {
        console.error('ChatConfig chưa được inject từ server. Kiểm tra View.');
        return;
    }

    ChatState.currentUserId = config.currentUserId;
    ChatState.currentDisplayName = config.currentDisplayName;

    // Khởi tạo roomNameMap từ danh sách rooms server trả về
    (config.rooms || []).forEach(r => {
        ChatState.roomNameMap[r.id] = r.name;
    });

    // 2. Khởi tạo SignalR (chat-signalr.js)
    initSignalR();

    // 3. Gắn events
    _bindEvents();
});

// =====================================================
// EVENT BINDING
// =====================================================

function _bindEvents() {
    // Gửi tin nhắn khi nhấn Enter
    document.getElementById('messageInput').addEventListener('keydown', function (e) {
        if (e.key === 'Enter' && !e.shiftKey) {
            e.preventDefault();
            sendMessage();
        }
    });

    // Typing indicator (debounce 2s)
    document.getElementById('messageInput').addEventListener('input', function () {
        startTypingNotify();
    });

    // Browse rooms modal: load danh sách khi mở
    document.getElementById('browseRoomsModal').addEventListener('show.bs.modal', loadAvailableRooms);
}
