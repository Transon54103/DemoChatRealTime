/**
 * chat-utils.js
 * Các helper functions dùng chung, không phụ thuộc DOM hay state.
 */

/**
 * Escape HTML để chống XSS khi render nội dung user nhập vào.
 * @param {string} text
 * @returns {string}
 */
function escapeHtml(text) {
    if (!text) return '';
    const d = document.createElement('div');
    d.textContent = text;
    return d.innerHTML;
}

/**
 * Format thời gian theo locale vi-VN.
 * @param {string|Date} dateValue
 * @returns {string}
 */
function formatTime(dateValue) {
    return new Date(dateValue).toLocaleTimeString('vi-VN', {
        hour: '2-digit',
        minute: '2-digit'
    });
}
