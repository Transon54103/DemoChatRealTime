/**
 * chat-signalr.js
 * Quản lý toàn bộ kết nối SignalR: khởi tạo, xử lý events, reconnect.
 *
 * Phụ thuộc: chat-utils.js, chat-ui.js
 * Đọc/ghi state: ChatState (khai báo trong chat-init.js)
 * Gọi sang: chat-rooms.js (selectRoom, refreshRoomList)
 */

/**
 * Khởi tạo SignalR connection và đăng ký tất cả event handlers.
 * Được gọi 1 lần duy nhất từ chat-init.js.
 */
function initSignalR() {
    ChatState.connection = new signalR.HubConnectionBuilder()
        .withUrl('/chatHub')
        .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
        .configureLogging(signalR.LogLevel.Warning)
        .build();

    _registerSignalREvents();
    _registerConnectionStateHandlers();

    startSignalRConnection();
}

/**
 * Đăng ký các event nhận từ server.
 */
function _registerSignalREvents() {
    const conn = ChatState.connection;

    // Nhận tin nhắn mới
    conn.on('ReceiveMessage', function (msg) {
        if (msg.chatRoomId === ChatState.currentRoomId) {
            // Dedup: bỏ qua nếu đã render rồi (xảy ra khi reconnect)
            if (document.querySelector(`[data-message-id="${msg.id}"]`)) return;
            appendMessage(msg);
            scrollToBottom();
        } else {
            // Tin nhắn ở room khác → tăng unread + toast
            incrementUnread(msg.chatRoomId);
            showToast(msg.chatRoomId, msg.senderName, msg.content);
        }
        updateRoomPreview(msg.chatRoomId, msg.content);
    });

    // Online / Offline users
    conn.on('UserOnline', function (user) { addOnlineUser(user); });
    conn.on('UserOffline', function (user) { removeOnlineUser(user.userId); });

    // Typing indicator
    conn.on('UserTyping', function (data) {
        if (data.roomId === ChatState.currentRoomId) {
            showTypingIndicator(data.displayName);
        }
    });

    conn.on('UserStoppedTyping', function (data) {
        if (data.roomId === ChatState.currentRoomId) {
            hideTypingIndicator();
        }
    });

    // Có người vào phòng
    conn.on('UserJoinedRoom', function (data) {
        if (data.roomId === ChatState.currentRoomId) {
            appendSystemMessage(`${data.displayName} đã tham gia phòng`);
        }
    });

    // Lỗi từ server khi gửi tin
    conn.on('MessageError', function (error) {
        showToast(0, 'Lỗi', error);
    });

    // Đã join room thành công → refresh sidebar
    conn.on('JoinedRoom', function () {
        refreshRoomList();
    });
}

/**
 * Đăng ký các handler cho trạng thái kết nối.
 */
function _registerConnectionStateHandlers() {
    const conn = ChatState.connection;

    conn.onreconnecting(() =>
        updateConnectionStatus('reconnecting', '⟳ Đang kết nối lại...')
    );

    conn.onreconnected(() =>
        updateConnectionStatus('connected', '✓ Đã kết nối')
    );

    conn.onclose(() => {
        updateConnectionStatus('disconnected', '✗ Mất kết nối');
        setTimeout(startSignalRConnection, 5000);
    });
}

/**
 * Bắt đầu (hoặc thử lại) kết nối SignalR.
 * Tự retry mỗi 5 giây nếu thất bại.
 */
async function startSignalRConnection() {
    try {
        await ChatState.connection.start();
        updateConnectionStatus('connected', '✓ Đã kết nối');
        document.getElementById('sendBtn').disabled = false;
    } catch (err) {
        updateConnectionStatus('disconnected', '✗ Lỗi kết nối');
        console.error('SignalR connection error:', err);
        setTimeout(startSignalRConnection, 5000);
    }
}
