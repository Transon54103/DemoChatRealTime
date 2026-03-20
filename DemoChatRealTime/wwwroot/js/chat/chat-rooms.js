/**
 * chat-rooms.js
 * Quản lý rooms: chọn room, tải messages, gửi tin, typing,
 * tạo room, browse & join room, refresh danh sách.
 *
 * Phụ thuộc: chat-utils.js, chat-ui.js, chat-signalr.js
 * Đọc/ghi state: ChatState (khai báo trong chat-init.js)
 */

// =====================================================
// SELECT ROOM
// =====================================================

/**
 * Chọn và mở một phòng chat.
 * @param {number} roomId
 * @param {string} roomName
 */
function selectRoom(roomId, roomName) {
    try {
        ChatState.currentRoomId = roomId;
        ChatState.oldestMessageId = 0;

        // Reset unread badge
        ChatState.unreadCounts[roomId] = 0;
        updateUnreadBadge(roomId);

        // Hiện khung chat, ẩn empty state
        showChatContent();

        // Cập nhật tiêu đề
        document.getElementById('currentRoomName').textContent = roomName;
        document.getElementById('messagesContainer').innerHTML = '';
        hideTypingIndicator();

        // Highlight room đang chọn trong sidebar
        document.querySelectorAll('.room-item').forEach(item => {
            item.classList.toggle('active', parseInt(item.dataset.roomId) === roomId);
        });

        loadMessages(roomId);
        document.getElementById('messageInput').focus();
    } catch (err) {
        console.error('selectRoom error:', err);
    }
}

// =====================================================
// LOAD MESSAGES (cursor-based pagination)
// =====================================================

/**
 * Tải messages của room, hỗ trợ cursor-based pagination (beforeId).
 * @param {number} roomId
 * @param {number} beforeId - 0 = tải mới nhất; > 0 = tải cũ hơn
 */
async function loadMessages(roomId, beforeId = 0) {
    try {
        const res = await fetch(`/Chat/GetMessages?roomId=${roomId}&beforeId=${beforeId}&pageSize=50`);
        if (!res.ok) return;

        const messages = await res.json();

        // Bỏ qua nếu user đã chuyển sang room khác trong khi đang tải
        if (roomId !== ChatState.currentRoomId) return;

        const container = document.getElementById('messagesContainer');

        if (beforeId === 0) {
            container.innerHTML = '';
        }
         
        container.querySelector('.load-more-btn')?.remove();

        // Nếu còn messages cũ hơn → hiện nút "Tải thêm"
        if (messages.length >= 50) {
            const btn = document.createElement('button');
            btn.className = 'load-more-btn';
            btn.textContent = '🔄 Tải thêm tin nhắn cũ';
            btn.onclick = () => loadMessages(roomId, ChatState.oldestMessageId);
            container.prepend(btn);
        }

        messages.forEach(msg => appendMessage(msg, beforeId > 0));

        if (messages.length > 0) {
            ChatState.oldestMessageId = messages[0].id;
        }

        if (beforeId === 0) scrollToBottom(true);
    } catch (err) {
        console.error('loadMessages error:', err);
    }
}

// =====================================================
// SEND MESSAGE
// =====================================================

/**
 * Gửi tin nhắn tới room hiện tại qua SignalR.
 */
async function sendMessage() {
    const input = document.getElementById('messageInput');
    const content = input.value.trim();
    if (!content || !ChatState.currentRoomId) return;

    input.value = '';
    input.focus();
    stopTypingNotify();

    try {
        await ChatState.connection.invoke('SendMessage', {
            content: content,
            chatRoomId: ChatState.currentRoomId
        });
    } catch (err) {
        console.error('sendMessage error:', err);
        input.value = content; // Khôi phục nội dung nếu gửi lỗi
        showToast(0, 'Lỗi', 'Không thể gửi tin nhắn. Kiểm tra kết nối.');
    }
}

// =====================================================
// TYPING INDICATOR (debounce)
// =====================================================

function startTypingNotify() {
    if (!ChatState.currentRoomId) return;

    if (!ChatState.isTyping) {
        ChatState.isTyping = true;
        ChatState.connection.invoke('StartTyping', ChatState.currentRoomId).catch(() => {});
    }

    clearTimeout(ChatState.typingTimeout);
    ChatState.typingTimeout = setTimeout(stopTypingNotify, 2000);
}

function stopTypingNotify() {
    if (ChatState.isTyping && ChatState.currentRoomId) {
        ChatState.isTyping = false;
        ChatState.connection.invoke('StopTyping', ChatState.currentRoomId).catch(() => {});
    }
    clearTimeout(ChatState.typingTimeout);
}

// =====================================================
// CREATE ROOM
// =====================================================

/**
 * Tạo phòng chat mới từ modal.
 */
async function createRoom() {
    const nameInput = document.getElementById('newRoomName');
    const name = nameInput.value.trim();
    if (!name) return;

    try {
        const res = await fetch('/Chat/CreateRoom', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ name })
        });

        if (res.ok) {
            const room = await res.json();
            nameInput.value = '';
            bootstrap.Modal.getInstance(document.getElementById('createRoomModal')).hide();

            ChatState.roomNameMap[room.id] = room.name;
            addRoomToSidebar(room);
            await ChatState.connection.invoke('JoinRoom', room.id);
            selectRoom(room.id, room.name);
        } else {
            showToast(0, 'Lỗi', 'Không thể tạo phòng');
        }
    } catch (err) {
        console.error('createRoom error:', err);
    }
}

// =====================================================
// BROWSE & JOIN ROOMS
// =====================================================

/**
 * Tải danh sách rooms chưa join vào modal Browse.
 */
async function loadAvailableRooms() {
    const container = document.getElementById('browseRoomsList');
    container.innerHTML = '<p class="text-muted text-center">Đang tải...</p>';

    try {
        const res = await fetch('/Chat/GetAvailableRooms');
        const rooms = await res.json();

        if (rooms.length === 0) {
            container.innerHTML = '<p class="text-muted text-center py-3">Bạn đã tham gia tất cả phòng!</p>';
            return;
        }

        container.innerHTML = '';
        rooms.forEach(room => {
            const div = document.createElement('div');
            div.className = 'browse-room-item';
            div.innerHTML = `
                <div class="room-info">
                    <strong>${escapeHtml(room.name)}</strong><br/>
                    <small>${room.memberCount} thành viên</small>
                </div>
                <button class="btn btn-sm btn-outline-primary"
                    onclick="joinRoom(${room.id}, '${escapeHtml(room.name).replace(/'/g, "\\'")}')">
                    Tham gia
                </button>
            `;
            container.appendChild(div);
        });
    } catch (err) {
        container.innerHTML = '<p class="text-danger text-center">Lỗi tải danh sách phòng</p>';
        console.error('loadAvailableRooms error:', err);
    }
}

/**
 * Join một phòng và chuyển vào phòng đó.
 * @param {number} roomId
 * @param {string} roomName
 */
async function joinRoom(roomId, roomName) {
    try {
        await ChatState.connection.invoke('JoinRoom', roomId);
        ChatState.roomNameMap[roomId] = roomName;
        bootstrap.Modal.getInstance(document.getElementById('browseRoomsModal')).hide();
        await refreshRoomList();
        selectRoom(roomId, roomName);
    } catch (err) {
        console.error('joinRoom error:', err);
    }
}

// =====================================================
// REFRESH ROOM LIST
// =====================================================

/**
 * Fetch lại danh sách rooms của user và render lại sidebar.
 */
async function refreshRoomList() {
    try {
        const res = await fetch('/Chat/GetMyRooms');
        const rooms = await res.json();
        renderRoomList(rooms);
    } catch (err) {
        console.error('refreshRoomList error:', err);
    }
}
