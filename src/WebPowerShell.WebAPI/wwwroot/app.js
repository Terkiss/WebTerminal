// WebPowerShell Frontend App Logic

// API Paths Config
const API = {
    AUTH_CHECK: '/api/weatherforecast',
    LOGIN: '/api/auth/login',
    LOGOUT: '/api/auth/logout',
    CHANGE_PASSWORD: '/api/auth/change-password',
    TERMINAL_HUB: '/hubs/terminal',
    PREFERENCES: '/api/users/preferences'
};

// Global State
const state = {
    connection: null,
    tabs: new Map(), // tabId (string) -> Tab instance
    activeTabId: null,
    username: 'Administrator',
    isAdmin: false,
    preferences: null,
    isPageSuspended: false,
    mobileControls: {
        ctrlPending: false,
        collapsed: false
    }
};

const MOBILE_OUTPUT_BUFFER_LIMIT = 256 * 1024;
const TERMINAL_WRITE_FLUSH_MS = 8;
const CTRL_KEY_MAP = {
    'c': '\x03',
    '[': '\x1b'
};

function applyCtrlModifier(sequence) {
    const normalized = sequence.toLowerCase();
    if (CTRL_KEY_MAP[normalized]) {
        return CTRL_KEY_MAP[normalized];
    }
    if (normalized.length === 1) {
        const code = normalized.charCodeAt(0);
        if (code >= 97 && code <= 122) {
            return String.fromCharCode(code - 96);
        }
    }
    return sequence;
}

// Cryptographically Strong UUID Generator Fallback
function generateUUID() {
    if (typeof crypto !== 'undefined') {
        if (crypto.randomUUID) {
            return crypto.randomUUID();
        }
        if (crypto.getRandomValues) {
            return ([1e7]+-1e3+-4e3+-8e3+-1e11).replace(/[018]/g, c =>
                (c ^ crypto.getRandomValues(new Uint8Array(1))[0] & 15 >> c / 4).toString(16)
            );
        }
    }
    // Fallback for extremely legacy environments
    return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function(c) {
        const r = Math.random() * 16 | 0;
        const v = c === 'x' ? r : (r & 0x3 | 0x8);
        return v.toString(16);
    });
}

// Toast Notification System
function showToast(message, type = 'info', duration = 3000) {
    if (state.isPageSuspended) {
        return;
    }

    const container = document.getElementById('toastContainer');
    const toast = document.createElement('div');
    toast.className = `toast toast-${type}`;
    
    let icon = 'fa-info-circle';
    if (type === 'success') icon = 'fa-circle-check';
    if (type === 'error') icon = 'fa-triangle-exclamation';
    
    toast.innerHTML = `
        <span class="toast-icon"><i class="fa-solid ${icon}"></i></span>
        <span class="toast-content"></span>
    `;
    toast.querySelector('.toast-content').textContent = message;
    
    container.appendChild(toast);
    
    // Animate in
    setTimeout(() => toast.classList.add('show'), 50);
    
    // Auto remove
    setTimeout(() => {
        toast.classList.remove('show');
        setTimeout(() => toast.remove(), 400);
    }, duration);
}

// Check auth status on start
async function checkInitialAuth() {
    try {
        const response = await fetch(API.AUTH_CHECK);
        if (response.ok) {
            // Already logged in
            // Try to extract username or fallback
            // In a real app we might have a user endpoint, but here we can just show the app
            showAppView();
        } else {
            showLoginView();
        }
    } catch (e) {
        showLoginView();
    }
}

function showLoginView() {
    document.getElementById('loginOverlay').classList.add('active');
    document.getElementById('appContainer').classList.add('hidden');
    document.getElementById('adminPanelShortcut')?.classList.add('hidden');
}

async function showAppView(options = {}) {
    const { connect = true, requirePasswordChange = false } = options;

    document.getElementById('loginOverlay').classList.remove('active');
    document.getElementById('appContainer').classList.remove('hidden');

    if (requirePasswordChange) {
        openChangePasswordModal(true);
    }
    
    if (connect) {
        try {
            const prefRes = await fetch(API.PREFERENCES);
            if (prefRes.ok) {
                state.preferences = await prefRes.json();
            }
        } catch(e) { console.warn('Failed to load preferences', e); }
    }
    
    const displayUser = document.getElementById('welcomeUser'); // Fixed ID from index.html
    if (displayUser) {
        displayUser.textContent = `Welcome, ${state.username}`;
    }
    
    const adminBtn = document.getElementById('adminBtn');
    if (adminBtn) {
        adminBtn.style.display = state.isAdmin ? 'inline-block' : 'none';
    }
    const adminPanelShortcut = document.getElementById('adminPanelShortcut');
    if (adminPanelShortcut) {
        adminPanelShortcut.classList.toggle('hidden', !state.isAdmin);
    }
    
    if (connect) {
        initSignalR();
    }
}

function openChangePasswordModal(required = false) {
    const overlay = document.getElementById('changePasswordOverlay');
    const cancelBtn = document.getElementById('btnCancelChangePassword');
    const errorMsg = document.getElementById('changePasswordErrorMsg');

    if (!overlay) return;

    overlay.classList.add('active');
    overlay.dataset.required = required ? 'true' : 'false';

    if (cancelBtn) {
        cancelBtn.disabled = required;
    }
    if (errorMsg && required) {
        errorMsg.querySelector('span').textContent = 'Password change is required before opening terminal sessions.';
        errorMsg.classList.remove('hidden');
    }
}

// SignalR Client Logic
function initSignalR() {
    if (state.connection) {
        return;
    }
    
    updateConnectionBadge('connecting', 'Connecting');
    
    state.connection = new signalR.HubConnectionBuilder()
        .withUrl(API.TERMINAL_HUB)
        .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
        .build();
        
    // Listeners
    state.connection.on("TerminalOutput", (tabId, chunk) => {
        const tab = state.tabs.get(tabId);
        if (!tab || !chunk) return;

        let output = chunk;
        if (typeof chunk === 'string') {
            try {
                const binary = window.atob(chunk);
                const bytes = new Uint8Array(binary.length);
                for (let i = 0; i < binary.length; i++) {
                    bytes[i] = binary.charCodeAt(i);
                }
                output = tab.decoder.decode(bytes, { stream: true });
            } catch (e) {
                console.warn('Failed to decode terminal output payload:', e);
                return;
            }
        }

        tab.writeOrBuffer(output, true);
    });
    
    state.connection.on("TerminalExited", (tabId, exitCode) => {
        const tab = state.tabs.get(tabId);
        if (tab) {
            tab.writeOrBuffer(`\r\n\x1b[31m[Process exited with code ${exitCode}]\x1b[0m\r\n`);
            tab.isRunning = false;
        }
    });
    

    
    // Connection State Handling
    state.connection.onreconnecting((error) => {
        updateConnectionBadge('connecting', 'Reconnecting');
        showToast('Connection lost. Reconnecting...', 'error');
    });
    
    state.connection.onreconnected((connectionId) => {
        updateConnectionBadge('connected', 'Connected');
        showToast('Connection restored.', 'success');
        // Optionally reopen active tabs to guarantee session synchronization
        syncActiveSessions();
    });
    
    state.connection.onclose((error) => {
        updateConnectionBadge('disconnected', 'Disconnected');
        showToast('Session connection terminated.', 'error');
        // Retry connection manually after 10s
        setTimeout(() => {
            if (document.getElementById('loginOverlay').classList.contains('hidden')) {
                startConnection();
            }
        }, 10000);
    });
    
    startConnection();
}

async function startConnection() {
    try {
        await state.connection.start();
        updateConnectionBadge('connected', 'Connected');
        showToast('Secure terminal session authorized.', 'success');
        
        // Check for restorable sessions from previous server run
        await restorePersistedSessions();
        
        // If no sessions were restored, open a fresh tab
        if (state.tabs.size === 0) {
            await createNewTab();
            setTimeout(() => {
                showToast('검은색 터미널 화면을 마우스로 클릭하신 후, 키보드로 직접 명령어를 타이핑하십시오.', 'info', 6000);
            }, 1000);
        }
    } catch (err) {
        updateConnectionBadge('disconnected', 'Disconnected');
        showToast('Failed to connect to terminal service.', 'error');
        console.error(err);
    }
}

// Restore/attach sessions: live sessions from other devices + persisted sessions from previous server run
async function restorePersistedSessions() {
    try {
        const sessions = await state.connection.invoke("ListSessions");
        if (!sessions || sessions.length === 0) return;
        
        const total = sessions.length;
        showToast(`Found ${total} existing session(s). Reconnecting...`, 'info', 3000);
        
        let firstTabId = null;
        for (let i = 0; i < sessions.length; i++) {
            const s = sessions[i];
            const tabId = s.sessionId;
            const name = `Session ${i + 1}`;
            
            // Skip if this tab is already loaded locally
            if (state.tabs.has(tabId)) continue;
            
            // Create Tab UI
            const tab = new Tab(tabId, name);
            state.tabs.set(tabId, tab);
            tab.initializeDOM();
            
            if (!firstTabId) firstTabId = tabId;
            
            try {
                // Activate tab so xterm.open() is called before output arrives
                switchTab(tabId);
                
                if (s.isLive) {
                    // Session is live on the server (e.g., created from another device)
                    // Just attach to the existing process
                    const response = await state.connection.invoke("AttachSession", tabId);
                    if (response && response.success === false) {
                        tab.writeOrBuffer(`\r\n\x1b[31m[Attach failed: ${response.errorMessage || 'Unknown'}]\x1b[0m\r\n`);
                    } else {
                        tab.writeOrBuffer(`\x1b[32m[Live session attached — ${s.workingDirectory}]\x1b[0m\r\n`);
                    }
                } else {
                    // Session is persisted from a previous server run — create new process at saved directory
                    const response = await state.connection.invoke("RestoreSession", tabId, s.workingDirectory);
                    if (response && response.success === false) {
                        tab.writeOrBuffer(`\r\n\x1b[31m[Restore failed: ${response.errorMessage || 'Unknown'}]\x1b[0m\r\n`);
                    } else {
                        tab.writeOrBuffer(`\x1b[36m[Session restored — ${s.workingDirectory}]\x1b[0m\r\n`);
                    }
                }
            } catch (e) {
                console.error(`Failed to connect session ${tabId}:`, e);
                tab.writeOrBuffer(`\r\n\x1b[31m[Connection failed: ${e.message}]\x1b[0m\r\n`);
            }
        }
        
        // Switch to the first tab
        if (firstTabId) {
            switchTab(firstTabId);
        }
        
        showToast(`${total} session(s) connected successfully.`, 'success');
    } catch (e) {
        console.error('Failed to restore sessions:', e);
        // Non-critical — fall through to create a new tab
    }
}

function updateConnectionBadge(status, text) {
    const badge = document.getElementById('connectionStatusBadge');
    if (!badge) return;
    
    const dot = badge.querySelector('.status-dot');
    const label = badge.querySelector('.status-text');
    
    dot.className = 'status-dot';
    dot.classList.add(`dot-${status}`);
    label.textContent = text;
}

// Sync existing tabs with backend when reconnecting
async function syncActiveSessions() {
    for (const [tabId, tab] of state.tabs.entries()) {
        try {
            await state.connection.invoke("AttachSession", tabId);
            tab.writeOrBuffer("\r\n\x1b[33m[Connection Restored]\x1b[0m\r\n");
        } catch (e) {
            console.error(`Failed to sync tab ${tabId}:`, e);
        }
    }
}

class Tab {
    constructor(id, name) {
        this.id = id;
        this.name = name;
        this.isRunning = true;
        this.terminal = null;
        this.domElement = null;
        this.tabItemEl = null;
        this.isOpened = false; // flag to track if xterm open has been called
        this.isRenderReady = false; // flag: fit() has completed at least once
        this.pendingWrites = []; // buffer for writes before render is ready
        this.suspendedWrites = [];
        this.suspendedWriteBytes = 0;
        this.writeQueue = [];
        this.writeFlushTimer = null;
        this.encoder = new TextEncoder();
        this.decoder = new TextDecoder();
    }
    
    initializeDOM() {
        // 1. Create sidebar list item
        const tabList = document.getElementById('tabList');
        const item = document.createElement('div');
        item.className = 'tab-item';
        item.id = `tab-item-${this.id}`;
        item.innerHTML = `
            <div class="tab-info">
                <span class="tab-icon"><i class="fa-solid fa-terminal"></i></span>
                <span class="tab-name">${this.name}</span>
            </div>
            <button class="tab-close" title="Close Session"><i class="fa-solid fa-xmark"></i></button>
        `;
        
        item.addEventListener('click', (e) => {
            if (e.target.closest('.tab-close')) {
                closeTab(this.id);
            } else {
                switchTab(this.id);
                // Mobile auto-close sidebar on tab selection
                const sidebar = document.querySelector('.sidebar');
                const overlay = document.getElementById('sidebarOverlay');
                if (sidebar && sidebar.classList.contains('active')) {
                    sidebar.classList.remove('active');
                    overlay.classList.remove('active');
                }
            }
        });
        
        tabList.appendChild(item);
        this.tabItemEl = item;
        
        // 2. Create terminal workspace element
        const containerWrapper = document.getElementById('terminalContainers');
        const container = document.createElement('div');
        container.className = 'terminal-container-el';
        container.id = `terminal-container-${this.id}`;
        containerWrapper.appendChild(container);
        this.domElement = container;
        
        // 3. Initialize Xterm (but do NOT open yet)
        const themeBg = state.preferences?.themeBackground || '#090d16';
        const themeFg = state.preferences?.themeForeground || '#cbd5e1';
        const fontSize = state.preferences?.fontSize || 14;

        this.terminal = new Terminal({
            cursorBlink: true,
            cursorStyle: 'bar',
            fontSize: fontSize,
            fontFamily: "'Fira Code', 'JetBrains Mono', Courier New, monospace",
            theme: {
                background: themeBg,
                foreground: themeFg,
                cursor: '#00f2fe',
                selectionBackground: 'rgba(0, 242, 254, 0.25)',
                black: '#0f172a',
                red: '#ef4444',
                green: '#10b981',
                yellow: '#f59e0b',
                blue: '#3b82f6',
                magenta: '#d946ef',
                cyan: '#06b6d4',
                white: '#f8fafc'
            }
        });

        // Load FitAddon
        this.fitAddon = new FitAddon.FitAddon();
        this.terminal.loadAddon(this.fitAddon);
        
        // Handle User Input directly piped to PTY
        this.terminal.onData(async (data) => {
            await this.sendInput(data);
        });
        
        // Handle Resize
        this.terminal.onResize(async (size) => {
            if (state.isPageSuspended) {
                return;
            }
            if (!state.connection || state.connection.state !== signalR.HubConnectionState.Connected) {
                return;
            }
            try {
                await state.connection.invoke("Resize", this.id, size.cols, size.rows);
            } catch (e) {
                console.error("Failed to send resize", e);
            }
        });
    }

    async sendInput(data) {
        if (state.isPageSuspended) {
            return;
        }
        if (!state.connection || state.connection.state !== signalR.HubConnectionState.Connected) {
            return;
        }
        try {
            const payload = this.encoder.encode(data);
            let binary = '';
            for (let i = 0; i < payload.length; i++) {
                binary += String.fromCharCode(payload[i]);
            }
            const base64 = window.btoa(binary);
            await state.connection.invoke("SendInput", this.id, base64);
        } catch (e) {
            console.error("Failed to send input:", e);
        }
    }

    bufferOutput(output, replayable = true) {
        if (output.length > MOBILE_OUTPUT_BUFFER_LIMIT) {
            output = output.slice(output.length - MOBILE_OUTPUT_BUFFER_LIMIT);
        }
        this.suspendedWrites.push({ output, replayable });
        if (replayable) {
            this.suspendedWriteBytes += output.length;
        }
        while (this.suspendedWriteBytes > MOBILE_OUTPUT_BUFFER_LIMIT && this.suspendedWrites.length > 1) {
            const removeIndex = this.suspendedWrites.findIndex((entry) => entry.replayable);
            if (removeIndex === -1) break;
            const removed = this.suspendedWrites.splice(removeIndex, 1)[0];
            this.suspendedWriteBytes -= removed.output.length;
        }
    }

    writeOrBuffer(output, replayableWhenSuspended = false) {
        if (state.isPageSuspended || !this.isRenderReady) {
            if (state.isPageSuspended) {
                this.bufferOutput(output, replayableWhenSuspended);
            } else {
                this.pendingWrites.push(output);
            }
            return;
        }
        this.enqueueWrite(output);
    }

    enqueueWrite(output) {
        if (!output || !this.terminal) return;

        this.writeQueue.push(output);
        if (this.writeFlushTimer !== null) {
            return;
        }

        this.writeFlushTimer = setTimeout(() => {
            this.writeFlushTimer = null;
            this.flushWriteQueue();
        }, TERMINAL_WRITE_FLUSH_MS);
    }

    flushWriteQueue() {
        if (!this.terminal || this.writeQueue.length === 0) {
            return;
        }

        const output = this.writeQueue.length === 1
            ? this.writeQueue.pop()
            : this.writeQueue.splice(0).join('');

        this.terminal.write(output);
    }

    flushPendingWrites() {
        const toFlush = [
            ...this.pendingWrites.splice(0),
            ...this.suspendedWrites.splice(0).map((entry) => entry.output)
        ];
        this.suspendedWriteBytes = 0;
        if (toFlush.length > 0) {
            this.enqueueWrite(toFlush.join(''));
        }
    }

    clearReplayableSuspendedWrites() {
        this.suspendedWrites = this.suspendedWrites.filter((entry) => !entry.replayable);
        this.suspendedWriteBytes = 0;
    }

    setSuspended(suspended, flushBufferedOutput = true) {
        if (!this.terminal) return;
        this.terminal.options.cursorBlink = !suspended;
        if (suspended) {
            this.flushWriteQueue();
        }
        if (!suspended && flushBufferedOutput && this.isOpened) {
            this.flushPendingWrites();
        }
    }

    async attachIfConnected() {
        if (!state.connection || state.connection.state !== signalR.HubConnectionState.Connected) {
            return false;
        }
        try {
            await state.connection.invoke("AttachSession", this.id);
            return true;
        } catch (e) {
            console.error(`Failed to attach session ${this.id}:`, e);
            return false;
        }
    }

    async resizeToCurrentFit() {
        if (state.isPageSuspended || !this.terminal || !this.isOpened) {
            return;
        }
        this.fit();
        const size = { cols: this.terminal.cols, rows: this.terminal.rows };
        try {
            if (!state.connection || state.connection.state !== signalR.HubConnectionState.Connected) {
                return;
            }
            await state.connection.invoke("Resize", this.id, size.cols, size.rows);
        } catch (e) {
            console.error("Failed to send resize", e);
        }
    }
    
    fit() {
        if (state.isPageSuspended || !this.domElement || !this.isOpened) return;
        try {
            if (this.domElement.offsetWidth === 0 || this.domElement.offsetHeight === 0) {
                return;
            }
            this.fitAddon.fit();
        } catch (e) {
            console.warn("FitAddon error:", e);
        }
    }

    scheduleFit() {
        if (state.isPageSuspended) return;
        requestAnimationFrame(() => {
            this.fit();
            // Mark render-ready and flush pending writes after first fit
            if (!this.isRenderReady) {
                this.isRenderReady = true;
                if (this.pendingWrites.length > 0 || this.suspendedWrites.length > 0) {
                    this.flushPendingWrites();
                }
            }
            setTimeout(() => this.fit(), 80);
        });
    }
    
    setActive(active) {
        if (active) {
            this.tabItemEl.classList.add('active');
            this.domElement.classList.add('active');
            
            // Open and initialize xterm ONLY when it is active (visible in DOM)
            if (!this.isOpened) {
                this.terminal.open(this.domElement);
                this.isOpened = true;
            }
            
            if (!state.isPageSuspended) {
                this.terminal.focus();
            }
            this.scheduleFit();
        } else {
            this.tabItemEl.classList.remove('active');
            this.domElement.classList.remove('active');
        }
    }
    
    cleanup() {
        if (this.terminal) {
            this.terminal.dispose();
        }
        if (this.tabItemEl) {
            this.tabItemEl.remove();
        }
        if (this.domElement) {
            this.domElement.remove();
        }
    }
}

// Create New Tab Session
async function createNewTab() {
    if (!state.connection || state.connection.state !== signalR.HubConnectionState.Connected) {
        showToast('Cannot create session: Disconnected from terminal service.', 'error');
        return;
    }
    
    const id = generateUUID();
    const name = `Session ${state.tabs.size + 1}`;
    
    // Instantiate and register tab before sending SignalR request to prevent output drops
    const newTab = new Tab(id, name);
    state.tabs.set(id, newTab);
    newTab.initializeDOM();
    
    // Switch to the tab immediately so that terminal.open() is executed BEFORE backend sends output
    switchTab(id);
    
    try {
        const response = await state.connection.invoke("CreateSession", id);
        if (response && response.success === false) {
            showToast(`Failed to open session: ${response.errorMessage || 'Unknown error'}`, 'error');
            newTab.cleanup();
            state.tabs.delete(id);
            return;
        }
    } catch (e) {
        showToast(`Failed to instantiate terminal session: ${e.message || e}`, 'error', 7000);
        newTab.cleanup();
        state.tabs.delete(id);
        console.error(e);
    }
}

// Switch Active Tab
function switchTab(tabId) {
    if (!state.tabs.has(tabId)) return;
    
    if (state.activeTabId) {
        const prevTab = state.tabs.get(state.activeTabId);
        if (prevTab) prevTab.setActive(false);
    }
    
    state.activeTabId = tabId;
    const tab = state.tabs.get(tabId);
    tab.setActive(true);
    
    // Update Header UI
    document.getElementById('currentTabTitle').textContent = tab.name;
    
    // Sync header progress bar and cancel button
    const progress = document.getElementById('executionProgressBar');
    const stopBtn = document.getElementById('btnStopCommand');
    
    if (tab.isRunning) {
        progress.classList.add('active');
        stopBtn.disabled = false;
    } else {
        progress.classList.remove('active');
        stopBtn.disabled = true;
    }
}

// Close Session Tab
async function closeTab(tabId) {
    const tab = state.tabs.get(tabId);
    if (!tab) return;
    
    try {
        if (state.connection && state.connection.state === signalR.HubConnectionState.Connected) {
            await state.connection.invoke("CloseSession", tabId);
        }
    } catch (e) {
        console.error(`Failed to notify backend close tab:`, e);
    }
    
    tab.cleanup();
    state.tabs.delete(tabId);
    
    // Switch to another tab if available
    if (state.activeTabId === tabId) {
        state.activeTabId = null;
        if (state.tabs.size > 0) {
            const firstId = state.tabs.keys().next().value;
            switchTab(firstId);
        } else {
            document.getElementById('currentTabTitle').textContent = 'No Session';
            document.getElementById('executionProgressBar').classList.remove('active');
            document.getElementById('btnStopCommand').disabled = true;
        }
    }
}

// Execute command on session
async function executeCommand(tabId, command) {
    // Deprecated for raw PTY stream. Input is handled in terminal.onData
}

// Abort execution (Ctrl+C trigger)
async function abortExecution(tabId) {
    const tab = state.tabs.get(tabId);
    if (!tab) return;
    
    await tab.sendInput('\x03');
}

// Clear terminal viewport
function clearActiveTerminal() {
    if (!state.activeTabId) return;
    const tab = state.tabs.get(state.activeTabId);
    if (tab && tab.terminal) {
        tab.terminal.clear();
    }
}

function getActiveTab() {
    if (!state.activeTabId) return null;
    return state.tabs.get(state.activeTabId) || null;
}

async function sendMobileInput(sequence) {
    const tab = getActiveTab();
    if (!tab || state.isPageSuspended) return;

    let input = sequence;
    if (state.mobileControls.ctrlPending) {
        input = applyCtrlModifier(sequence);
        setMobileCtrlPending(false);
    }

    await tab.sendInput(input);
    if (tab.terminal && !state.isPageSuspended) {
        tab.terminal.focus();
    }
}

function setMobileCtrlPending(active) {
    state.mobileControls.ctrlPending = active;
    const ctrlBtn = document.getElementById('mobileKeyCtrl');
    const status = document.getElementById('mobileCtrlStatus');
    if (ctrlBtn) {
        ctrlBtn.classList.toggle('active', active);
        ctrlBtn.setAttribute('aria-pressed', String(active));
    }
    if (status) {
        status.textContent = active ? 'Ctrl armed' : 'Ctrl off';
    }
}

function setMobileControlsCollapsed(collapsed) {
    state.mobileControls.collapsed = collapsed;
    const panel = document.getElementById('mobileKeypad');
    const toggle = document.getElementById('mobileKeypadToggle');
    if (!panel || !toggle) return;
    panel.classList.toggle('collapsed', collapsed);
    toggle.setAttribute('aria-expanded', String(!collapsed));
    toggle.title = collapsed ? 'Open mobile keypad' : 'Close mobile keypad';
}

function initMobileKeypad() {
    const panel = document.getElementById('mobileKeypad');
    if (!panel) return;
    panel.addEventListener('contextmenu', (event) => event.preventDefault());

    const bindings = {
        mobileKeyUp: '\x1b[A',
        mobileKeyDown: '\x1b[B',
        mobileKeyRight: '\x1b[C',
        mobileKeyLeft: '\x1b[D',
        mobileKeyTab: '\x09',
        mobileKeyEsc: '\x1b',
        mobileKeyEnter: '\x0d',
        mobileKeyCtrlC: 'c'
    };

    for (const [id, sequence] of Object.entries(bindings)) {
        const button = document.getElementById(id);
        if (!button) continue;
        button.addEventListener('pointerdown', (event) => {
            event.preventDefault();
            if (event.isPrimary === false) return;
            sendMobileInput(sequence);
        });
    }

    const ctrlBtn = document.getElementById('mobileKeyCtrl');
    if (ctrlBtn) {
        ctrlBtn.addEventListener('pointerdown', (event) => {
            event.preventDefault();
            if (event.isPrimary === false) return;
            if (state.isPageSuspended) return;
            setMobileCtrlPending(!state.mobileControls.ctrlPending);
        });
    }

    const toggle = document.getElementById('mobileKeypadToggle');
    if (toggle) {
        toggle.addEventListener('pointerdown', (event) => {
            event.preventDefault();
            if (event.isPrimary === false) return;
            if (state.isPageSuspended) return;
            setMobileControlsCollapsed(!state.mobileControls.collapsed);
        });
    }
}

async function resumeVisiblePage() {
    if (!state.isPageSuspended) return;
    state.isPageSuspended = false;
    document.body.classList.remove('app-suspended');
    for (const tab of state.tabs.values()) {
        tab.setSuspended(false, false);
    }
    for (const tab of state.tabs.values()) {
        const attached = await tab.attachIfConnected();
        if (attached) {
            tab.clearReplayableSuspendedWrites();
            tab.flushPendingWrites();
        } else {
            tab.flushPendingWrites();
        }
    }
    const active = getActiveTab();
    if (active) {
        await active.resizeToCurrentFit();
    }
}

function suspendHiddenPage() {
    if (state.isPageSuspended) return;
    state.isPageSuspended = true;
    document.body.classList.add('app-suspended');
    setMobileCtrlPending(false);
    for (const tab of state.tabs.values()) {
        tab.setSuspended(true);
    }
}

function handlePageLifecycle() {
    if (document.hidden) {
        suspendHiddenPage();
    } else {
        resumeVisiblePage();
    }
}

// Clear state and logout
function logoutApp() {
    // Cleanup sessions
    for (const tabId of state.tabs.keys()) {
        closeTab(tabId);
    }
    
    if (state.connection) {
        state.connection.stop();
        state.connection = null;
    }
    
    state.activeTabId = null;
    updateConnectionBadge('disconnected', 'Disconnected');
    showLoginView();
    showToast('Secure session successfully logged out.', 'info');
}

// Global Event Listeners & Bootstrapping
document.addEventListener('DOMContentLoaded', () => {
    // 1. Initial login check
    checkInitialAuth();
    
    // 2. Login Form submit
    const loginForm = document.getElementById('loginForm');
    if (loginForm) {
        loginForm.addEventListener('submit', async (e) => {
            e.preventDefault();
            const usernameInput = document.getElementById('loginUsername');
            const passwordInput = document.getElementById('loginPassword');
            const errorMsg = document.getElementById('loginErrorMsg');
            const submitBtn = document.getElementById('btnLoginSubmit');
            
            submitBtn.disabled = true;
            errorMsg.classList.add('hidden');
            
            try {
                const response = await fetch(API.LOGIN, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({
                        username: usernameInput.value,
                        password: passwordInput.value
                    })
                });
                
                if (response.ok) {
                    const data = await response.json();
                    state.username = data.username || usernameInput.value;
                    state.isAdmin = data.isAdmin === true;
                    passwordInput.value = '';
                    if (data.isPasswordExpired === true) {
                        showAppView({ connect: false, requirePasswordChange: true });
                    } else {
                        showAppView();
                    }
                } else {
                    let errMsg = 'Authentication failed. Please verify credentials.';
                    try {
                        const errorData = await response.json();
                        if (errorData && errorData.message) errMsg = errorData.message;
                    } catch(err) {}
                    
                    errorMsg.querySelector('span').textContent = errMsg;
                    errorMsg.classList.remove('hidden');
                }
            } catch (err) {
                errorMsg.querySelector('span').textContent = 'Server connection failed.';
                errorMsg.classList.remove('hidden');
                console.error(err);
            } finally {
                submitBtn.disabled = false;
            }
        });
    }
    
    // 3. Logout action
    const logoutBtn = document.getElementById('btnLogout');
    if (logoutBtn) {
        logoutBtn.addEventListener('click', async () => {
            try {
                await fetch(API.LOGOUT, { method: 'POST' });
            } catch (e) {
                console.error(e);
            }
            logoutApp();
        });
    }
    
    // 4. Change Password Modals
    const btnOpenChangePw = document.getElementById('btnOpenChangePassword');
    const overlayChangePw = document.getElementById('changePasswordOverlay');
    const btnCancelChangePw = document.getElementById('btnCancelChangePassword');
    const changePwForm = document.getElementById('changePasswordForm');
    
    // Elements for mobile close automation
    const sidebar = document.querySelector('.sidebar');
    const sidebarOverlay = document.getElementById('sidebarOverlay');
    
    if (btnOpenChangePw && overlayChangePw) {
        btnOpenChangePw.addEventListener('click', () => {
            openChangePasswordModal(false);
            if (sidebar && sidebar.classList.contains('active')) {
                sidebar.classList.remove('active');
                sidebarOverlay.classList.remove('active');
            }
        });
    }
    if (btnCancelChangePw && overlayChangePw) {
        btnCancelChangePw.addEventListener('click', () => {
            if (overlayChangePw.dataset.required === 'true') {
                return;
            }
            overlayChangePw.classList.remove('active');
            changePwForm.reset();
            document.getElementById('changePasswordErrorMsg').classList.add('hidden');
        });
    }
    if (changePwForm && overlayChangePw) {
        changePwForm.addEventListener('submit', async (e) => {
            e.preventDefault();
            const oldPasswordInput = document.getElementById('oldPassword');
            const newPasswordInput = document.getElementById('newPassword');
            const confirmInput = document.getElementById('confirmNewPassword');
            const errorMsg = document.getElementById('changePasswordErrorMsg');
            const submitBtn = document.getElementById('btnChangePasswordSubmit');
            
            errorMsg.classList.add('hidden');
            
            if (newPasswordInput.value !== confirmInput.value) {
                errorMsg.querySelector('span').textContent = 'Confirm password does not match.';
                errorMsg.classList.remove('hidden');
                return;
            }
            
            submitBtn.disabled = true;
            
            try {
                const response = await fetch(API.CHANGE_PASSWORD, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({
                        currentPassword: oldPasswordInput.value,
                        newPassword: newPasswordInput.value
                    })
                });
                
                if (response.ok) {
                    showToast('Password successfully updated.', 'success');
                    overlayChangePw.classList.remove('active');
                    overlayChangePw.dataset.required = 'false';
                    const cancelBtn = document.getElementById('btnCancelChangePassword');
                    if (cancelBtn) {
                        cancelBtn.disabled = false;
                    }
                    changePwForm.reset();
                    await showAppView();
                } else {
                    let errMsg = 'Failed to change password. Old password may be incorrect.';
                    try {
                        const errorData = await response.json();
                        if (errorData && (errorData.message || errorData.Message)) {
                            errMsg = errorData.message || errorData.Message;
                        }
                    } catch(err) {}
                    
                    errorMsg.querySelector('span').textContent = errMsg;
                    errorMsg.classList.remove('hidden');
                }
            } catch (err) {
                errorMsg.querySelector('span').textContent = 'Server connection failed.';
                errorMsg.classList.remove('hidden');
            } finally {
                submitBtn.disabled = false;
            }
        });
    }
    
    // Preferences Modals
    const btnOpenSettings = document.getElementById('btnOpenSettings');
    const overlaySettings = document.getElementById('settingsOverlay');
    const btnCancelSettings = document.getElementById('btnCancelSettings');
    const settingsForm = document.getElementById('settingsForm');
    const prefFontSize = document.getElementById('prefFontSize');
    const prefFontSizeVal = document.getElementById('prefFontSizeVal');
    const prefThemeBackground = document.getElementById('prefThemeBackground');
    const prefThemeForeground = document.getElementById('prefThemeForeground');

    if (prefFontSize && prefFontSizeVal) {
        prefFontSize.addEventListener('input', (e) => {
            prefFontSizeVal.textContent = e.target.value;
        });
    }

    if (btnOpenSettings && overlaySettings) {
        btnOpenSettings.addEventListener('click', () => {
            if (state.preferences) {
                prefFontSize.value = state.preferences.fontSize || 14;
                prefFontSizeVal.textContent = state.preferences.fontSize || 14;
                prefThemeBackground.value = state.preferences.themeBackground || '#090d16';
                prefThemeForeground.value = state.preferences.themeForeground || '#cbd5e1';
            }
            overlaySettings.classList.add('active');
            if (sidebar && sidebar.classList.contains('active')) {
                sidebar.classList.remove('active');
                sidebarOverlay.classList.remove('active');
            }
        });
    }
    
    if (btnCancelSettings && overlaySettings) {
        btnCancelSettings.addEventListener('click', () => {
            overlaySettings.classList.remove('active');
        });
    }
    
    if (settingsForm && overlaySettings) {
        settingsForm.addEventListener('submit', async (e) => {
            e.preventDefault();
            const btnSaveSettings = document.getElementById('btnSaveSettings');
            btnSaveSettings.disabled = true;
            
            const newPrefs = {
                fontSize: parseInt(prefFontSize.value, 10),
                themeBackground: prefThemeBackground.value,
                themeForeground: prefThemeForeground.value
            };
            
            try {
                const response = await fetch(API.PREFERENCES, {
                    method: 'PUT',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(newPrefs)
                });
                
                if (response.ok) {
                    state.preferences = newPrefs;
                    // Apply to all existing tabs
                    for (const tab of state.tabs.values()) {
                        tab.terminal.options.fontSize = state.preferences.fontSize;
                        const currentTheme = tab.terminal.options.theme;
                        tab.terminal.options.theme = Object.assign({}, currentTheme, {
                            background: state.preferences.themeBackground,
                            foreground: state.preferences.themeForeground
                        });
                        tab.fit(); // refit with new font size
                    }
                    showToast('Preferences saved.', 'success');
                    overlaySettings.classList.remove('active');
                } else {
                    showToast('Failed to save preferences.', 'error');
                }
            } catch (err) {
                showToast('Server connection failed.', 'error');
            } finally {
                btnSaveSettings.disabled = false;
            }
        });
    }

    // 5. App Dashboard actions
    const btnNewTab = document.getElementById('btnNewTab');
    if (btnNewTab) {
        btnNewTab.addEventListener('click', () => {
            createNewTab();
            if (sidebar && sidebar.classList.contains('active')) {
                sidebar.classList.remove('active');
                sidebarOverlay.classList.remove('active');
            }
        });
    }
    
    const btnClear = document.getElementById('btnClearScreen');
    if (btnClear) {
        btnClear.addEventListener('click', () => clearActiveTerminal());
    }
    
    const btnStop = document.getElementById('btnStopCommand');
    if (btnStop) {
        btnStop.addEventListener('click', () => {
            if (state.activeTabId) {
                abortExecution(state.activeTabId);
            }
        });
    }
    
    // 6. Window resize handler (debounced)
    let resizeTimeout;
    window.addEventListener('resize', () => {
        if (state.isPageSuspended) {
            return;
        }
        clearTimeout(resizeTimeout);
        resizeTimeout = setTimeout(() => {
            for (const tab of state.tabs.values()) {
                tab.fit();
            }
        }, 150);
    });
    
    // 7. Shortcut Key Bindings
    window.addEventListener('keydown', (e) => {
        // Prevent default browser behavior for terminal app shortcuts
        if (e.ctrlKey && e.key === 't') { // Ctrl+T New Tab
            e.preventDefault();
            if (!document.getElementById('appContainer').classList.contains('hidden')) {
                createNewTab();
            }
        }
    });
    
    // 8. Mobile Sidebar Toggle & Overlay logic
    const btnToggleSidebar = document.getElementById('btnToggleSidebar');
    if (btnToggleSidebar && sidebarOverlay && sidebar) {
        btnToggleSidebar.addEventListener('click', () => {
            sidebar.classList.toggle('active');
            sidebarOverlay.classList.toggle('active');
        });
        
        sidebarOverlay.addEventListener('click', () => {
            sidebar.classList.remove('active');
            sidebarOverlay.classList.remove('active');
        });
    }

    initMobileKeypad();
    document.addEventListener('visibilitychange', handlePageLifecycle);
    window.addEventListener('pagehide', suspendHiddenPage);
    window.addEventListener('pageshow', resumeVisiblePage);
    window.addEventListener('freeze', suspendHiddenPage);
    window.addEventListener('resume', resumeVisiblePage);



    // 9. Admin Dashboard
    const adminBtn = document.getElementById('adminBtn');
    const adminDashboard = document.getElementById('adminDashboardOverlay');
    const btnShowCreateUser = document.getElementById('btnShowCreateUser');
    const btnBackToUsers = document.getElementById('btnBackToUsers');
    const btnRefreshSessions = document.getElementById('btnRefreshSessions');
    
    // Tabs in Dashboard
    const dashTabs = document.querySelectorAll('.dash-tab');
    dashTabs.forEach(tab => {
        tab.addEventListener('click', () => {
            dashTabs.forEach(t => t.classList.remove('active'));
            tab.classList.add('active');
            
            document.querySelectorAll('.dash-pane').forEach(p => {
                p.classList.remove('active');
                p.style.display = 'none';
            });
            const targetId = tab.getAttribute('data-target');
            const target = document.getElementById(targetId);
            if (target) {
                target.style.display = 'block';
                // Trigger reflow
                void target.offsetWidth;
                target.classList.add('active');
            }
            
            if (targetId === 'dash-users') loadAdminUsers();
            if (targetId === 'dash-sessions') loadAdminSessions();
        });
    });

    if (adminBtn && adminDashboard) {
        adminBtn.addEventListener('click', () => {
            adminDashboard.classList.add('active');
            // reset to users tab
            if(dashTabs.length > 0) dashTabs[0].click();
        });
    }

    if (btnShowCreateUser) {
        btnShowCreateUser.addEventListener('click', () => {
            document.getElementById('dash-users').style.display = 'none';
            document.getElementById('dash-users').classList.remove('active');
            document.getElementById('dash-create-user').style.display = 'block';
            void document.getElementById('dash-create-user').offsetWidth;
            document.getElementById('dash-create-user').classList.add('active');
            document.getElementById('createAuthError').classList.add('hidden');
            document.getElementById('createAuthSuccess').classList.add('hidden');
            document.getElementById('createUserForm').reset();
        });
    }

    if (btnBackToUsers) {
        btnBackToUsers.addEventListener('click', () => {
            if(dashTabs.length > 0) dashTabs[0].click();
        });
    }

    if (btnRefreshSessions) {
        btnRefreshSessions.addEventListener('click', loadAdminSessions);
    }

    const createUserForm = document.getElementById('createUserForm');
    if (createUserForm) {
        createUserForm.addEventListener('submit', async (e) => {
            e.preventDefault();
            const submitBtn = createUserForm.querySelector('button[type="submit"]');
            const errorMsg = document.getElementById('createAuthError');
            const successMsg = document.getElementById('createAuthSuccess');
            
            submitBtn.disabled = true;
            errorMsg.classList.add('hidden');
            successMsg.classList.add('hidden');
            
            try {
                const response = await fetch('/api/users', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({
                        username: document.getElementById('newUsername').value,
                        password: document.getElementById('newUserPassword').value,
                        isAdmin: document.getElementById('newUserIsAdmin').checked
                    })
                });
                
                if (response.ok) {
                    successMsg.classList.remove('hidden');
                    createUserForm.reset();
                    setTimeout(() => dashTabs[0].click(), 1500); // go back to list
                } else {
                    let errMsg = 'Failed to create user.';
                    try {
                        const errorData = await response.json();
                        if (errorData && errorData.message) errMsg = errorData.message;
                    } catch(err) {}
                    errorMsg.querySelector('span').textContent = errMsg;
                    errorMsg.classList.remove('hidden');
                }
            } catch (err) {
                errorMsg.querySelector('span').textContent = 'Server connection failed.';
                errorMsg.classList.remove('hidden');
            } finally {
                submitBtn.disabled = false;
            }
        });
    }

    // Load functions
    async function loadAdminUsers() {
        const tbody = document.getElementById('adminUsersTableBody');
        if (!tbody) return;
        tbody.innerHTML = '<tr><td colspan="5" style="text-align: center;">Loading...</td></tr>';
        
        try {
            const res = await fetch('/api/admin/users');
            if (res.ok) {
                const users = await res.json();
                tbody.innerHTML = '';
                users.forEach(u => {
                    const tr = document.createElement('tr');
                    const roleBadge = u.isAdmin ? '<span class="badge badge-admin">Admin</span>' : '<span class="badge badge-user">User</span>';
                    const statusBadge = u.isActive ? '<span class="badge badge-active">Active</span>' : '<span class="badge" style="background: rgba(239, 68, 68, 0.15); color: #f87171; border: 1px solid rgba(239, 68, 68, 0.3)">Inactive</span>';
                    
                    tr.innerHTML = `
                        <td>${u.username}</td>
                        <td>${roleBadge}</td>
                        <td>${statusBadge}</td>
                        <td>${new Date(u.createdAt).toLocaleDateString()}</td>
                        <td>
                            <button class="btn-danger-sm btn-delete-user" data-id="${u.id}" ${u.username === state.username ? 'disabled title="Cannot delete yourself"' : ''}>
                                <i class="fa-solid fa-trash-can"></i> Delete
                            </button>
                        </td>
                    `;
                    tbody.appendChild(tr);
                });

                // Attach delete handlers
                document.querySelectorAll('.btn-delete-user').forEach(btn => {
                    btn.addEventListener('click', async (e) => {
                        const id = e.currentTarget.getAttribute('data-id');
                        if (confirm('Are you sure you want to delete this user?')) {
                            try {
                                const delRes = await fetch(`/api/admin/users/${id}`, { method: 'DELETE' });
                                if (delRes.ok) {
                                    showToast('User deleted', 'success');
                                    loadAdminUsers();
                                } else {
                                    showToast('Failed to delete user', 'error');
                                }
                            } catch (err) {
                                showToast('Server error', 'error');
                            }
                        }
                    });
                });
            } else {
                tbody.innerHTML = '<tr><td colspan="5" style="text-align: center; color: var(--danger-color);">Failed to load users</td></tr>';
            }
        } catch (e) {
            tbody.innerHTML = '<tr><td colspan="5" style="text-align: center; color: var(--danger-color);">Error connecting to server</td></tr>';
        }
    }

    async function loadAdminSessions() {
        const tbody = document.getElementById('adminSessionsTableBody');
        if (!tbody) return;
        tbody.innerHTML = '<tr><td colspan="5" style="text-align: center;">Loading...</td></tr>';
        
        try {
            const res = await fetch('/api/admin/sessions');
            if (res.ok) {
                const sessions = await res.json();
                tbody.innerHTML = '';
                if (sessions.length === 0) {
                    tbody.innerHTML = '<tr><td colspan="5" style="text-align: center;">No active sessions</td></tr>';
                    return;
                }

                sessions.forEach(s => {
                    const tr = document.createElement('tr');
                    const connBadge = s.hasConnections ? '<span class="badge badge-active">Attached</span>' : '<span class="badge" style="background: rgba(245, 158, 11, 0.15); color: #fbbf24; border: 1px solid rgba(245, 158, 11, 0.3)">Detached</span>';
                    
                    tr.innerHTML = `
                        <td style="font-family: monospace; font-size: 0.85em;">${s.sessionId.substring(0,8)}...</td>
                        <td>${s.ownerUserId.substring(0,8)}...</td>
                        <td>${connBadge} (${s.connectionCount})</td>
                        <td>${new Date(s.lastActivityAt).toLocaleTimeString()}</td>
                        <td>
                            <button class="btn-danger-sm btn-kill-session" data-id="${s.sessionId}">
                                <i class="fa-solid fa-power-off"></i> Kill
                            </button>
                        </td>
                    `;
                    tbody.appendChild(tr);
                });

                // Attach kill handlers
                document.querySelectorAll('.btn-kill-session').forEach(btn => {
                    btn.addEventListener('click', async (e) => {
                        const id = e.currentTarget.getAttribute('data-id');
                        if (confirm('Are you sure you want to kill this session?')) {
                            try {
                                const killRes = await fetch(`/api/admin/sessions/${id}`, { method: 'DELETE' });
                                if (killRes.ok) {
                                    showToast('Session killed', 'success');
                                    loadAdminSessions();
                                } else {
                                    showToast('Failed to kill session', 'error');
                                }
                            } catch (err) {
                                showToast('Server error', 'error');
                            }
                        }
                    });
                });
            } else {
                tbody.innerHTML = '<tr><td colspan="5" style="text-align: center; color: var(--danger-color);">Failed to load sessions</td></tr>';
            }
        } catch (e) {
            tbody.innerHTML = '<tr><td colspan="5" style="text-align: center; color: var(--danger-color);">Error connecting to server</td></tr>';
        }
    }

    // File Manager Logic
    const fm = {
        currentPath: '/',
        container: document.getElementById('fileExplorerWorkspace'),
        toggleBtn: document.getElementById('btnToggleFileManager'),
        upBtn: document.getElementById('btnNavUp'),
        refreshBtn: document.getElementById('btnRefreshFiles'),
        uploadBtn: document.getElementById('btnUploadFile'),
        fileInput: document.getElementById('fileUploadInput'),
        pathDisplay: document.getElementById('currentPathDisplay'),
        listBody: document.getElementById('fileListBody'),

        async init() {
            if (!this.container) return;
            this.toggleBtn.addEventListener('click', () => this.toggle());
            this.upBtn.addEventListener('click', () => this.navigateUp());
            this.refreshBtn.addEventListener('click', () => this.loadFiles());
            this.uploadBtn.addEventListener('click', () => this.fileInput.click());
            this.fileInput.addEventListener('change', (e) => this.uploadFiles(e.target.files));
            
            // Auto-load on init
            this.loadFiles();
        },

        toggle() {
            this.container.classList.toggle('active');
            // If active, resize terminal to fit new available space
            setTimeout(() => {
                window.dispatchEvent(new Event('resize'));
            }, 300);
        },

        async loadFiles(path = this.currentPath) {
            try {
                const res = await fetch(`/api/files/list?path=${encodeURIComponent(path)}`);
                if (res.ok) {
                    const data = await res.json();
                    this.currentPath = data.currentPath;
                    this.pathDisplay.textContent = this.currentPath;
                    this.render(data.items);
                } else {
                    showToast('Failed to load directory', 'error');
                }
            } catch (e) {
                showToast('Error loading directory', 'error');
            }
        },

        render(items) {
            this.listBody.innerHTML = '';
            if (!items || items.length === 0) {
                this.listBody.innerHTML = '<tr><td colspan="4" style="text-align:center;">Directory is empty</td></tr>';
                return;
            }

            items.forEach(item => {
                const tr = document.createElement('tr');
                tr.className = 'file-item-row';
                const icon = item.isDirectory ? '<i class="fa-solid fa-folder file-icon folder"></i>' : '<i class="fa-solid fa-file file-icon file"></i>';
                const size = item.isDirectory ? '--' : this.formatSize(item.size);
                const date = new Date(item.lastModified).toLocaleDateString();

                tr.innerHTML = `
                    <td>${icon} <span class="file-name">${item.name}</span></td>
                    <td>${size}</td>
                    <td>${date}</td>
                    <td>
                        ${!item.isDirectory ? `<button class="file-action-btn" title="Download" onclick="fm_download('${item.path}')"><i class="fa-solid fa-download"></i></button>` : ''}
                        <button class="file-action-btn delete" title="Delete" onclick="fm_delete('${item.path}', ${item.isDirectory})"><i class="fa-solid fa-trash-can"></i></button>
                    </td>
                `;

                tr.addEventListener('dblclick', () => {
                    if (item.isDirectory) {
                        this.loadFiles(item.path);
                    }
                });

                this.listBody.appendChild(tr);
            });
        },

        navigateUp() {
            if (this.currentPath === '/' || this.currentPath === '') return;
            const parts = this.currentPath.split('/').filter(Boolean);
            parts.pop();
            const newPath = '/' + parts.join('/');
            this.loadFiles(newPath);
        },

        formatSize(bytes) {
            if (bytes === 0) return '0 B';
            const k = 1024;
            const sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
            const i = Math.floor(Math.log(bytes) / Math.log(k));
            return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
        },

        async uploadFiles(files) {
            if (!files || files.length === 0) return;

            let successCount = 0;
            let failCount = 0;

            for (let i = 0; i < files.length; i++) {
                const formData = new FormData();
                formData.append('file', files[i]);

                try {
                    const res = await fetch(`/api/files/upload?path=${encodeURIComponent(this.currentPath)}`, {
                        method: 'POST',
                        body: formData
                    });
                    if (res.ok) {
                        successCount++;
                    } else {
                        failCount++;
                    }
                } catch (e) {
                    failCount++;
                }
            }

            if (successCount > 0) {
                showToast(`${successCount} file(s) uploaded successfully`, 'success');
                this.loadFiles();
            }
            if (failCount > 0) {
                showToast(`${failCount} file(s) failed to upload`, 'error');
            }
            this.fileInput.value = ''; // reset
        }
    };

    // Make global for inline handlers
    window.fm_download = (path) => {
        window.location.href = `/api/files/download?path=${encodeURIComponent(path)}`;
    };
    
    window.fm_delete = async (path, isDir) => {
        if (confirm(`Delete ${isDir ? 'directory' : 'file'}?`)) {
            try {
                const res = await fetch(`/api/files/delete?path=${encodeURIComponent(path)}`, { method: 'DELETE' });
                if (res.ok) {
                    showToast('Deleted', 'success');
                    fm.loadFiles();
                } else {
                    showToast('Failed to delete', 'error');
                }
            } catch (e) {
                showToast('Error', 'error');
            }
        }
    };

    fm.init();
});
