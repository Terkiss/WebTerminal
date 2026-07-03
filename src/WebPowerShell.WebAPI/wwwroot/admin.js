document.addEventListener('DOMContentLoaded', () => {
    // Check Authentication (simple redirect if no token)
    const init = async () => {
        try {
            await fetchSystemMetrics();
            setupNavigation();
            setupRefresh();
            setupUserModal();
            
            // Initial loads
            fetchSessions();
            fetchUsers();
            
            // Auto refresh every 5s
            setInterval(() => {
                const activeNav = document.querySelector('.nav-item.active').dataset.target;
                if (activeNav === 'system-section') fetchSystemMetrics();
                else if (activeNav === 'sessions-section') fetchSessions();
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
                if (targetId === 'users-section') fetchUsers();
            });
        });
    };

    const setupRefresh = () => {
        document.getElementById('btn-refresh').addEventListener('click', () => {
            const activeNav = document.querySelector('.nav-item.active').dataset.target;
            if (activeNav === 'system-section') fetchSystemMetrics();
            if (activeNav === 'sessions-section') fetchSessions();
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
