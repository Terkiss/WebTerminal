// WebPowerShell Frontend App Logic

// API Paths Config
const API = {
    AUTH_CHECK: '/api/weatherforecast',
    LOGIN: '/api/auth/login',
    LOGOUT: '/api/auth/logout',
    CHANGE_PASSWORD: '/api/auth/change-password',
    TERMINAL_HUB: '/hubs/terminal'
};

// Global State
const state = {
    connection: null,
    tabs: new Map(), // tabId (string) -> Tab instance
    activeTabId: null,
    username: 'Administrator'
};

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
}

function showAppView() {
    document.getElementById('loginOverlay').classList.remove('active');
    document.getElementById('appContainer').classList.remove('hidden');
    
    const displayUser = document.getElementById('displayUsername');
    if (displayUser) {
        displayUser.textContent = state.username;
    }
    
    initSignalR();
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
        if (tab && chunk) {
            // SignalR provides 'chunk' as a Uint8Array or base64 string
            let data = chunk;
            if (typeof chunk === 'string') {
                // if it comes as base64 for some reason
                const binary_string = window.atob(chunk);
                const len = binary_string.length;
                data = new Uint8Array(len);
                for (let i = 0; i < len; i++) {
                    data[i] = binary_string.charCodeAt(i);
                }
            }
            tab.terminal.write(data);
        }
    });
    
    state.connection.on("TerminalExited", (tabId, exitCode) => {
        const tab = state.tabs.get(tabId);
        if (tab) {
            tab.terminal.write(`\r\n\x1b[31m[Process exited with code ${exitCode}]\x1b[0m\r\n`);
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
        
        // Open initial tab if none exists
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
            tab.terminal.write("\r\n\x1b[33m[Connection Restored]\x1b[0m\r\n");
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
        this.encoder = new TextEncoder();
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
        this.terminal = new Terminal({
            cursorBlink: true,
            cursorStyle: 'bar',
            fontSize: 14,
            fontFamily: "'Fira Code', 'JetBrains Mono', Courier New, monospace",
            theme: {
                background: '#090d16',
                foreground: '#cbd5e1',
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
        
        // Handle User Input directly piped to PTY
        this.terminal.onData(async (data) => {
            console.log('[DEBUG] onData fired:', JSON.stringify(data), 'connection state:', state.connection?.state);
            if (!state.connection || state.connection.state !== signalR.HubConnectionState.Connected) {
                console.warn('[DEBUG] onData: connection not ready, dropping input');
                return;
            }
            try {
                // Encode string to UTF-8 Uint8Array for binary transmission
                const payload = this.encoder.encode(data);
                
                // SignalR JSON protocol expects base64 string for byte[]
                let binary = '';
                for (let i = 0; i < payload.length; i++) {
                    binary += String.fromCharCode(payload[i]);
                }
                const base64 = window.btoa(binary);
                
                console.log('[DEBUG] Sending SendInput:', this.id, 'base64:', base64, 'raw bytes:', Array.from(payload));
                const result = await state.connection.invoke("SendInput", this.id, base64);
                console.log('[DEBUG] SendInput result:', result);
            } catch (e) {
                console.error("[DEBUG] Failed to send input:", e);
            }
        });
        
        // Handle Resize
        this.terminal.onResize(async (size) => {
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
    
    fit() {
        if (!this.domElement) return;
        const width = this.domElement.clientWidth;
        const height = this.domElement.clientHeight;
        
        // standard font calculations
        const cols = Math.max(40, Math.floor((width - 24) / 8.5));
        const rows = Math.max(10, Math.floor((height - 24) / 18));
        
        if (cols > 0 && rows > 0) {
            this.terminal.resize(cols, rows);
        }
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
            
            this.terminal.focus();
            this.fit();
            
            // Delay fitting to ensure DOM reflow has completed and container size is accurate
            setTimeout(() => {
                this.fit();
                if (this.terminal) {
                    this.terminal.refresh(0, this.terminal.rows - 1);
                }
            }, 60);
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
    
    try {
        if (state.connection && state.connection.state === signalR.HubConnectionState.Connected) {
            // 0x03 is Ctrl+C. Base64 of [3] is "Aw=="
            await state.connection.invoke("SendInput", tabId, "Aw==");
        }
    } catch (e) {
        showToast('Failed to cancel command.', 'error');
        console.error(e);
    }
}

// Clear terminal viewport
function clearActiveTerminal() {
    if (!state.activeTabId) return;
    const tab = state.tabs.get(state.activeTabId);
    if (tab && tab.terminal) {
        tab.terminal.clear();
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
                    passwordInput.value = '';
                    showAppView();
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
            overlayChangePw.classList.add('active');
            if (sidebar && sidebar.classList.contains('active')) {
                sidebar.classList.remove('active');
                sidebarOverlay.classList.remove('active');
            }
        });
    }
    if (btnCancelChangePw && overlayChangePw) {
        btnCancelChangePw.addEventListener('click', () => {
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
                    changePwForm.reset();
                } else {
                    let errMsg = 'Failed to change password. Old password may be incorrect.';
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
});
