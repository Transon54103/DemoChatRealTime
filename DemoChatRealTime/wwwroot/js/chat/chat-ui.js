/**
 * chat-ui.js
 * Tất cả thao tác DOM: render message, sidebar, toast, typing indicator,
 * connection status, online users. Không chứa logic gọi API hay SignalR.
 *
 * Phụ thuộc: chat-utils.js (escapeHtml, formatTime)
 * Đọc state từ: ChatState (khai báo trong chat-init.js)
 */

// =====================================================
// CONNECTION STATUS
// =====================================================

/**
 * Cập nhật thanh trạng thái kết nối SignalR.
 * @param {'connected'|'disconnected'|'reconnecting'} status
 * @param {string} text
 */
function updateConnectionStatus(status, text) {
    const el = document.getElementById('connectionStatus');
    el.className = `connection-status ${status}`;
    el.textContent = text;

    if (status === 'connected') {
        setTimeout(() => { el.style.display = 'none'; }, 2000);
    } else {
        el.style.display = 'block';
    }
}

// =====================================================
// MESSAGES
// =====================================================

/**
 * Render một tin nhắn vào khung chat.
 * @param {object} message - ChatMessageDto từ server
 * @param {boolean} prepend - true = chèn lên trên (load more cũ)
 */
function appendMessage(message, prepend = false) {
    const container = document.getElementById('messagesContainer');

    // Dedup: bỏ qua nếu đã render rồi
    if (document.querySelector(`[data-message-id="${message.id}"]`)) return;

    const isMine = message.senderId === ChatState.currentUserId;
    const div = document.createElement('div');
    div.className = `message ${isMine ? 'mine' : 'others'}`;
    div.dataset.messageId = message.id;

    div.innerHTML = `
        ${!isMine ? `<span class="sender-name">${escapeHtml(message.senderName)}</span>` : ''}
        <div class="bubble">${escapeHtml(message.content)}</div>
        <span class="time">${formatTime(message.sentAt)}</span>
    `;

    if (prepend) {
        const loadMoreBtn = container.querySelector('.load-more-btn');
        if (loadMoreBtn) loadMoreBtn.after(div);
        else container.prepend(div);
    } else {
        container.appendChild(div);
    }
}

/**
 * Render tin nhắn hệ thống (vd: "X đã tham gia phòng").
 * @param {string} text
 */
function appendSystemMessage(text) {
    const container = document.getElementById('messagesContainer');
    const div = document.createElement('div');
    div.className = 'system-message';
    div.textContent = `— ${text} —`;
    container.appendChild(div);
    scrollToBottom();
}

/**
 * Scroll xuống cuối khung chat.
 * @param {boolean} force - bỏ qua kiểm tra near-bottom, luôn scroll
 */
function scrollToBottom(force = false) {
    const c = document.getElementById('messagesContainer');
    const isNearBottom = c.scrollHeight - c.scrollTop - c.clientHeight < 150;
    if (force || isNearBottom) {
        requestAnimationFrame(() => { c.scrollTop = c.scrollHeight; });
    }
}

// =====================================================
// TYPING INDICATOR
// =====================================================

/**
 * Hiện thị "X đang gõ..." trong 3 giây.
 * @param {string} name
 */
function showTypingIndicator(name) {
    const el = document.getElementById('typingIndicator');
    el.textContent = `${name} đang gõ...`;
    setTimeout(() => {
        if (el.textContent.includes(name)) el.textContent = '';
    }, 3000);
}

function hideTypingIndicator() {
    document.getElementById('typingIndicator').textContent = '';
}

// =====================================================
// TOAST NOTIFICATIONS
// =====================================================

/**
 * Hiện toast notification khi nhận tin nhắn ở room khác.
 * Click vào toast sẽ chuyển tới room đó.
 * @param {number} roomId
 * @param {string} sender
 * @param {string} content
 */
function showToast(roomId, sender, content) {
    const container = document.getElementById('toastContainer');
    const roomName = ChatState.roomNameMap[roomId] || 'Chat';

    const toast = document.createElement('div');
    toast.className = 'chat-toast';
    toast.innerHTML = `
        <span class="toast-room">${escapeHtml(roomName)}</span>
        <br/><strong>${escapeHtml(sender)}</strong>:
        <span class="toast-msg">
            ${escapeHtml(content.substring(0, 80))}${content.length > 80 ? '...' : ''}
        </span>
    `;

    if (roomId > 0) {
        toast.style.cursor = 'pointer';
        toast.onclick = () => {
            selectRoom(roomId, roomName);
            toast.remove();
        };
    }

    container.appendChild(toast);
    setTimeout(() => toast.remove(), 4000);
}

// =====================================================
// UNREAD COUNT
// =====================================================

function incrementUnread(roomId) {
    ChatState.unreadCounts[roomId] = (ChatState.unreadCounts[roomId] || 0) + 1;
    updateUnreadBadge(roomId);
}

function updateUnreadBadge(roomId) {
    const badge = document.getElementById(`unread-${roomId}`);
    if (!badge) return;
    const count = ChatState.unreadCounts[roomId] || 0;
    badge.textContent = count > 99 ? '99+' : count;
    badge.classList.toggle('show', count > 0);
}

// =====================================================
// ROOM PREVIEW (sidebar)
// =====================================================

function updateRoomPreview(roomId, message) {
    const item = document.querySelector(`.room-item[data-room-id="${roomId}"]`);
    if (item) {
        const preview = item.querySelector('.room-preview');
        if (preview) preview.textContent = message;
    }
}

// =====================================================
// SIDEBAR - ROOM LIST
// =====================================================

/**
 * Thêm 1 room mới vào đầu danh sách sidebar (sau khi tạo room).
 * @param {object} room - ChatRoomDto
 */
function addRoomToSidebar(room) {
    const list = document.getElementById('roomList');
    const li = document.createElement('li');
    li.className = 'room-item';
    li.dataset.roomId = room.id;
    li.onclick = () => selectRoom(room.id, room.name);
    li.innerHTML = `
        <div class="d-flex justify-content-between align-items-center">
            <span class="room-name">${escapeHtml(room.name)}</span>
            <span class="room-meta">${room.memberCount} 👤</span>
        </div>
        <div class="room-preview">Chưa có tin nhắn</div>
        <span class="unread-badge" id="unread-${room.id}">0</span>
    `;
    list.prepend(li);
}

/**
 * Render lại toàn bộ danh sách rooms trong sidebar.
 * @param {Array} rooms - mảng ChatRoomDto
 */
function renderRoomList(rooms) {
    const list = document.getElementById('roomList');
    list.innerHTML = '';

    rooms.forEach(room => {
        ChatState.roomNameMap[room.id] = room.name;
        const li = document.createElement('li');
        li.className = `room-item ${room.id === ChatState.currentRoomId ? 'active' : ''}`;
        li.dataset.roomId = room.id;
        li.onclick = () => selectRoom(room.id, room.name);

        const unread = ChatState.unreadCounts[room.id] || 0;
        li.innerHTML = `
            <div class="d-flex justify-content-between align-items-center">
                <span class="room-name">${escapeHtml(room.name)}</span>
                <span class="room-meta">${room.memberCount} 👤</span>
            </div>
            <div class="room-preview">${escapeHtml(room.lastMessage || 'Chưa có tin nhắn')}</div>
            <span class="unread-badge ${unread > 0 ? 'show' : ''}" id="unread-${room.id}">
                ${unread}
            </span>
        `;
        list.appendChild(li);
    });
}

// =====================================================
// ONLINE USERS
// =====================================================

function addOnlineUser(user) {
    removeOnlineUser(user.userId);
    const list = document.getElementById('onlineUsersList');
    const div = document.createElement('div');
    div.className = 'online-user';
    div.id = `online-${user.userId}`;
    div.innerHTML = `<span class="online-dot"></span> ${escapeHtml(user.displayName)}`;
    list.appendChild(div);
    updateOnlineCount();
}

function removeOnlineUser(userId) {
    document.getElementById(`online-${userId}`)?.remove();
    updateOnlineCount();
}

function updateOnlineCount() {
    document.getElementById('onlineCount').textContent =
        document.querySelectorAll('#onlineUsersList .online-user').length;
}

// =====================================================
// CHAT CONTENT PANEL
// =====================================================

/**
 * Hiện khung chat (ẩn empty state).
 * Dùng class riêng thay vì Bootstrap d-none để tránh !important conflict.
 */
function showChatContent() {
    document.getElementById('emptyState').style.display = 'none';
    document.getElementById('chatContent').className = 'chat-content-visible';
}
