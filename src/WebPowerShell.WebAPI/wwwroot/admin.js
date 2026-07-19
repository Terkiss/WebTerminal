document.addEventListener('DOMContentLoaded', () => {
    // Check Authentication (simple redirect if no token)
    const init = async () => {
        try {
            setupNavigation();
            setupRefresh();
            setupUserModal();
            setupProviderActions();
            await fetchSystemMetrics();
            
            // Initial loads
            fetchSessions();
            fetchProviders();
            fetchUsers();
            
            // Auto refresh every 5s
            setInterval(() => {
                const activeNav = document.querySelector('.nav-item.active').dataset.target;
                if (activeNav === 'system-section') fetchSystemMetrics();
                else if (activeNav === 'sessions-section') fetchSessions();
                else if (activeNav === 'providers-section') fetchProviders();
            }, 5000);
            
        } catch (err) {
            console.error(err);
            if (err.status === 401 || err.status === 403) {
                alert('Unauthorized! Redirecting to terminal login...');
                window.location.href = '/';
            }
        }
    };

    // Navigation Logic
    const setupNavigation = () => {
        const navItems = document.querySelectorAll('.nav-item');
        const sections = document.querySelectorAll('.content-section');
        const pageTitle = document.getElementById('page-title');

        navItems.forEach(item => {
            item.addEventListener('click', (e) => {
                e.preventDefault();
                const targetId = item.dataset.target;
                
                navItems.forEach(nav => nav.classList.remove('active'));
                sections.forEach(sec => sec.classList.remove('active'));
                
                item.classList.add('active');
                document.getElementById(targetId).classList.add('active');
                pageTitle.textContent = item.querySelector('span').textContent;
                
                // Fetch data when tab opens
                if (targetId === 'system-section') fetchSystemMetrics();
                if (targetId === 'sessions-section') fetchSessions();
                if (targetId === 'providers-section') fetchProviders();
                if (targetId === 'users-section') fetchUsers();
            });
        });
    };

    const setupRefresh = () => {
        document.getElementById('btn-refresh').addEventListener('click', () => {
            const activeNav = document.querySelector('.nav-item.active').dataset.target;
            if (activeNav === 'system-section') fetchSystemMetrics();
            if (activeNav === 'sessions-section') fetchSessions();
            if (activeNav === 'providers-section') fetchProviders();
            if (activeNav === 'users-section') fetchUsers();
        });
    };

    // --- API Calls ---

    const fetchSystemMetrics = async () => {
        const res = await fetch('/api/admin/system');
        if (!res.ok) throw res;
        const data = await res.json();
        
        const cpuValue = document.getElementById('cpu-value');
        const cpuBar = document.getElementById('cpu-bar');
        const ramValue = document.getElementById('ram-value');
        const ramBar = document.getElementById('ram-bar');
        
        cpuValue.textContent = `${data.cpuUsagePercent.toFixed(1)}%`;
        cpuBar.style.width = `${data.cpuUsagePercent}%`;
        
        const usedRam = data.totalRamMb - data.availableRamMb;
        const ramPercent = (usedRam / data.totalRamMb) * 100;
        ramValue.textContent = `${usedRam.toFixed(0)} / ${data.totalRamMb.toFixed(0)} MB`;
        ramBar.style.width = `${ramPercent}%`;
    };

    const fetchSessions = async () => {
        const res = await fetch('/api/admin/sessions');
        if (!res.ok) throw res;
        const sessions = await res.json();
        
        document.getElementById('sessions-count').textContent = sessions.length;
        const tbody = document.getElementById('sessions-tbody');
        tbody.innerHTML = '';
        
        if (sessions.length === 0) {
            tbody.innerHTML = `<tr><td colspan="6" class="text-center">No active sessions</td></tr>`;
            return;
        }

        sessions.forEach(s => {
            const tr = document.createElement('tr');
            tr.innerHTML = `
                <td title="${s.sessionId}">${s.sessionId.substring(0, 8)}...</td>
                <td>${s.ownerUserId}</td>
                <td>${new Date(s.createdAt).toLocaleString()}</td>
                <td>${new Date(s.lastActivityAt).toLocaleTimeString()}</td>
                <td><span class="status ${s.hasConnections ? 'active' : 'inactive'}">${s.connectionCount} conn</span></td>
                <td>
                    <button class="btn-danger" onclick="killSession('${s.sessionId}')">Kill</button>
                </td>
            `;
            tbody.appendChild(tr);
        });
    };

    window.killSession = async (id) => {
        if (!confirm('Are you sure you want to force kill this session?')) return;
        try {
            const res = await fetch(`/api/admin/sessions/${id}`, { method: 'DELETE' });
            if (!res.ok) throw res;
            fetchSessions();
        } catch (e) {
            alert('Failed to kill session');
        }
    };

    const fetchProviders = async () => {
        const res = await fetch('/api/agent/provider-sessions');
        if (!res.ok) throw res;
        const providers = await res.json();

        const tbody = document.getElementById('providers-tbody');
        tbody.innerHTML = '';

        if (providers.length === 0) {
            tbody.innerHTML = `<tr><td colspan="5" class="text-center">No provider sessions</td></tr>`;
            return;
        }

        providers.forEach(provider => {
            const tr = document.createElement('tr');
            const conversation = provider.conversationId
                ? `${provider.conversationId.substring(0, 8)}...`
                : '--';
            const isInactive = provider.state === 'Failed' || provider.state === 'Stopped';

            tr.innerHTML = `
                <td>
                    <strong>${escapeHtml(provider.displayName)}</strong>
                    <div class="muted">${escapeHtml(provider.profile)}</div>
                </td>
                <td><span class="status ${isInactive ? 'inactive' : 'active'}">${escapeHtml(provider.state)}</span></td>
                <td title="${escapeHtml(provider.conversationId || '')}">${escapeHtml(conversation)}</td>
                <td>${new Date(provider.expiresAt).toLocaleString()}</td>
                <td>
                    <button class="btn-secondary" onclick="showProviderConfig('${provider.sessionId}')">Config</button>
                    <button class="btn-danger" onclick="revokeProvider('${provider.sessionId}')">Revoke</button>
                </td>
            `;
            tbody.appendChild(tr);
        });
    };

    const setupProviderActions = () => {
        const btnCreate = document.getElementById('btn-create-provider');
        if (!btnCreate) return;

        btnCreate.addEventListener('click', async () => {
            btnCreate.disabled = true;
            try {
                const res = await fetch('/api/agent/provider-sessions', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({
                        profile: 'agy-default',
                        displayName: `AGY Provider ${new Date().toLocaleTimeString()}`
                    })
                });

                if (!res.ok) throw res;
                const created = await res.json();
                showProviderKey(created);
                await fetchProviders();
            } catch (e) {
                alert('Failed to create provider session');
            } finally {
                btnCreate.disabled = false;
            }
        });
    };

    const showProviderKey = (created) => {
        const card = document.getElementById('provider-key-card');
        const key = document.getElementById('provider-api-key');
        key.textContent = formatProviderManifest(created.connectionManifest || created);
        card.classList.remove('hidden');
    };

    window.showProviderConfig = async (id) => {
        try {
            const res = await fetch(`/api/agent/provider-sessions/${id}`);
            if (!res.ok) throw res;
            const provider = await res.json();
            const card = document.getElementById('provider-key-card');
            const key = document.getElementById('provider-api-key');
            key.textContent = formatProviderManifest(provider.connectionManifest || provider);
            card.classList.remove('hidden');
        } catch (e) {
            alert('Failed to load provider config');
        }
    };

    window.revokeProvider = async (id) => {
        if (!confirm('Revoke this provider API key and stop the session?')) return;
        try {
            const res = await fetch(`/api/agent/provider-sessions/${id}`, { method: 'DELETE' });
            if (!res.ok) throw res;
            fetchProviders();
        } catch (e) {
            alert('Failed to revoke provider session');
        }
    };

    const escapeHtml = (value) => {
        return String(value ?? '')
            .replaceAll('&', '&amp;')
            .replaceAll('<', '&lt;')
            .replaceAll('>', '&gt;')
            .replaceAll('"', '&quot;')
            .replaceAll("'", '&#039;');
    };

    const formatProviderManifest = (manifest) => {
        const openAi = manifest.openAi || manifest.harness || {};
        const hookBridge = manifest.hookBridge || {};
        const smokeTest = manifest.smokeTest || {};
        const env = openAi.environment || {};
        const hookEnv = hookBridge.environment || {};

        return [
            `OPENAI_BASE_URL=${env.OPENAI_BASE_URL || manifest.baseUrl || ''}`,
            `OPENAI_API_KEY=${env.OPENAI_API_KEY || '<apiKey>'}`,
            `OPENAI_MODEL=${env.OPENAI_MODEL || manifest.model || 'agy'}`,
            `WEBTERMINAL_PROVIDER_BASE_URL=${env.WEBTERMINAL_PROVIDER_BASE_URL || manifest.baseUrl || ''}`,
            `WEBTERMINAL_PROVIDER_API_KEY=${env.WEBTERMINAL_PROVIDER_API_KEY || '<apiKey>'}`,
            '',
            `hook=${hookBridge.enabled ? 'enabled' : 'disabled'}`,
            `WEBTERMINAL_AGENT_EVENT_ENDPOINT=${hookEnv.WEBTERMINAL_AGENT_EVENT_ENDPOINT || hookBridge.endpoint || ''}`,
            `WEBTERMINAL_PROVIDER_SESSION_ID=${hookEnv.WEBTERMINAL_PROVIDER_SESSION_ID || manifest.sessionId || ''}`,
            `WEBTERMINAL_AGENT_EVENT_SECRET=${hookEnv.WEBTERMINAL_AGENT_EVENT_SECRET || '<configured server secret>'}`,
            `hook_command=${hookBridge.command || ''}`,
            '',
            `smoke_test=${smokeTest.command || ''}`
        ].join('\n');
    };

    const fetchUsers = async () => {
        const res = await fetch('/api/admin/users');
        if (!res.ok) throw res;
        const users = await res.json();
        
        const tbody = document.getElementById('users-tbody');
        tbody.innerHTML = '';
        
        if (users.length === 0) {
            tbody.innerHTML = `<tr><td colspan="5" class="text-center">No users found</td></tr>`;
            return;
        }

        users.forEach(u => {
            const tr = document.createElement('tr');
            const roleBadge = u.isAdmin ? `<span class="badge" style="background: rgba(236,72,153,0.2); color: #f472b6;">Admin</span>` : `<span class="badge">User</span>`;
            
            tr.innerHTML = `
                <td><strong>${u.username}</strong></td>
                <td><span class="status ${u.isActive ? 'active' : 'inactive'}">${u.isActive ? 'Active' : 'Locked'}</span></td>
                <td>${roleBadge}</td>
                <td>${new Date(u.createdAt).toLocaleDateString()}</td>
                <td>
                    <button class="btn-danger" onclick="deleteUser('${u.id}', '${u.username}')" ${u.username === 'terukiss' ? 'disabled' : ''}>Delete</button>
                </td>
            `;
            tbody.appendChild(tr);
        });
    };

    window.deleteUser = async (id, username) => {
        if (!confirm(`Are you sure you want to delete user '${username}'?`)) return;
        try {
            const res = await fetch(`/api/admin/users/${id}`, { method: 'DELETE' });
            if (!res.ok) throw res;
            fetchUsers();
        } catch (e) {
            alert('Failed to delete user');
        }
    };

    // User Modal Logic
    const setupUserModal = () => {
        const modal = document.getElementById('add-user-modal');
        const btnOpen = document.getElementById('btn-add-user');
        const btnClose = document.querySelector('.close-modal');
        const form = document.getElementById('add-user-form');

        btnOpen.addEventListener('click', () => modal.classList.add('show'));
        btnClose.addEventListener('click', () => modal.classList.remove('show'));
        
        window.addEventListener('click', (e) => {
            if (e.target === modal) modal.classList.remove('show');
        });

        form.addEventListener('submit', async (e) => {
            e.preventDefault();
            const username = document.getElementById('new-username').value;
            const password = document.getElementById('new-password').value;
            const isAdmin = document.getElementById('new-isadmin').checked;

            try {
                const res = await fetch('/api/admin/users', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ username, password, isAdmin })
                });

                if (!res.ok) {
                    const err = await res.json();
                    throw new Error(err.message || 'Creation failed');
                }
                
                modal.classList.remove('show');
                form.reset();
                fetchUsers();
            } catch (err) {
                alert(err.message);
            }
        });
    };

    init();
});
