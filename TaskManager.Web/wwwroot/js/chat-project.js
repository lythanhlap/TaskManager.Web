// wwwroot/js/chat-project.js
// Gọi: window.ChatProject.init({ projectId, actorId, memberIds, selectors?, hubUrl?, pageSize? })

window.ChatProject = (function () {
    // ----------------- Helpers -----------------
    function q(sel) { return document.querySelector(sel); }
    function el(tag, cls, html) {
        const e = document.createElement(tag);
        if (cls) e.className = cls;
        if (html !== undefined) e.innerHTML = html;
        return e;
    }
    function esc(s) { return (s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c])); }
    function initials(name) {
        const s = (name || '').trim();
        if (!s) return '?';
        const p = s.split(/\s+/);
        return ((p[0]?.[0] || '') + (p[1]?.[0] || '')).toUpperCase();
    }
    function avatarEl(name, url) {
        const wrap = el('div', 'avatar');
        if (url) {
            const img = el('img'); img.src = url; wrap.appendChild(img);
        } else {
            wrap.textContent = initials(name);
        }
        return wrap;
    }
    async function api(url, init) {
        const r = await fetch(url, init);
        if (!r.ok) throw new Error(`${init?.method || 'GET'} ${url} -> ${r.status}`);
        const ct = r.headers.get('Content-Type') || '';
        return ct.includes('application/json') ? r.json() : r.text();
    }

    // ----------------- Module state -----------------
    let opts, userMap = {}, dom = {}, convId = null, hub = null, lastSender = null;

    // ----------------- UI rendering -----------------
    function senderInfo(userId) {
        const u = userMap[userId] || {};
        return { name: u.name || userId, avatar: u.avatar || null };
    }

    function msgRow(m, me, prevSenderId) {
        const row = el('div', `msg ${me ? 'me' : 'other'}`);

        const info = senderInfo(m.senderUserId);
        if (!me) row.appendChild(avatarEl(info.name, info.avatar)); // left avatar for others

        const col = el('div');

        // Only show sender name when sender changes (like Messenger)
        const showName = !me && m.senderUserId !== prevSenderId;
        if (showName) {
            const nameEl = el('div', 'meta-name', esc(info.name));
            col.appendChild(nameEl);
        }

        const bubble = el('div', 'bubble',
            m.content === '[deleted]' ? '<i class="text-muted">[đã xoá]</i>' : esc(m.content)
        );
        col.appendChild(bubble);

        const meta = el('div', 'meta', new Date(m.sentAt).toLocaleString());
        col.appendChild(meta);

        row.appendChild(col);

        if (me) row.appendChild(avatarEl(senderInfo(opts.actorId).name, userMap[opts.actorId]?.avatar)); // right avatar for me
        return row;
    }

    function render(list) {
        dom.box.innerHTML = '';
        if (!Array.isArray(list) || list.length === 0) {
            dom.box.appendChild(el('div', 'text-muted small', 'Chưa có tin nhắn.'));
            return;
        }
        let prev = null;
        for (const m of list) {
            const me = m.senderUserId === opts.actorId;
            dom.box.appendChild(msgRow(m, me, prev));
            prev = m.senderUserId;
        }
        lastSender = prev;
        scrollToBottom();
    }

    function append(m) {
        if (!convId || m.conversationId !== convId) return;
        const me = m.senderUserId === opts.actorId;
        dom.box.appendChild(msgRow(m, me, lastSender));
        lastSender = m.senderUserId;
        scrollToBottom();
    }

    function scrollToBottom() { dom.box.scrollTop = dom.box.scrollHeight; }

    // ----------------- API operations -----------------
    async function ensureConversation() {
        // Find by name "proj:{projectId}" or create group
        const convName = `proj:${opts.projectId}`;
        const list = await api(`/api/chat/conversations?page=1&pageSize=300`);
        const found = list.find(c => c.name === convName);
        if (found) return found.id;

        const participants = [...new Set((opts.memberIds || []).filter(id => id && id !== opts.actorId))];
        const created = await api('/api/chat/conversations', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ kind: 2, name: convName, participantUserIds: participants })
        });
        return created.id;
    }

    async function loadMessages() {
        if (!convId) return;
        const msgs = await api(`/api/chat/messages/${convId}?take=${opts.pageSize}`);
        render(msgs);
        // mark read (best-effort)
        try { await api(`/api/chat/read/${convId}`, { method: 'POST' }); } catch { }
    }

    async function send() {
        const content = (dom.input.value || '').trim();
        if (!content || !convId) return;
        dom.input.value = '';
        const m = await api('/api/chat/messages', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ conversationId: convId, content })
        });
        append(m); // optimistic render
    }

    // ----------------- SignalR -----------------
    async function startHub() {
        if (!window.signalR) {
            console.warn('SignalR chưa được nạp. Thêm <script src="https://cdnjs.cloudflare.com/ajax/libs/microsoft-signalr/8.0.0/signalr.min.js"></script>.');
            return;
        }
        hub = new signalR.HubConnectionBuilder()
            .withUrl(opts.hubUrl)
            .withAutomaticReconnect()
            .build();

        //hub.on('message', append);
        hub.on('message', (msg) => {
            // Nếu là tin của chính mình thì bỏ qua (đã append lạc quan ở client)
            if (msg && msg.senderUserId === opts.actorId) return;
            append(msg);
        });
        hub.on('presence', () => { /* typing/read hook if needed */ });

        await hub.start();
        try { await hub.invoke('Join', convId); } catch { }
    }

    // ----------------- Public API -----------------
    async function init(initOpts) {
        // defaults
        opts = Object.assign({
            selectors: { box: '#chatMessages', input: '#chatInput', sendBtn: '#chatSendBtn' },
            hubUrl: '/hubs/chat',
            pageSize: 50
        }, initOpts || {});

        // cache dom
        dom.box = q(opts.selectors.box);
        dom.input = q(opts.selectors.input);
        dom.send = q(opts.selectors.sendBtn);
        if (!dom.box || !dom.input || !dom.send) return;

        // user map
        userMap = opts.userMap || {};
        // ensure actor exists in map
        if (opts.actorId && !userMap[opts.actorId]) {
            userMap[opts.actorId] = { name: 'Me', avatar: null };
        }

        // wire events
        dom.send.addEventListener('click', send);
        dom.input.addEventListener('keydown', (e) => {
            if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); send(); }
        });

        // boot
        dom.box.innerHTML = '<div class="text-muted small">Đang khởi tạo…</div>';
        try {
            convId = await ensureConversation();
            await startHub();
            await loadMessages();
        } catch (e) {
            console.error(e);
            dom.box.innerHTML = '<div class="text-danger small">Không khởi tạo được chat.</div>';
        }
    }

    return { init };
})();
