/*
 * Shared behaviour for the static mockups. Everything a screen's state depends on comes from the
 * URL, so each state has a link that html.to.design can capture:
 *
 *   ?theme=dark              dark theme
 *   ?brand=acme              a dedicated deployment's branding
 *   ?logo=a|b|c              logo candidate (see logo-options.html); remembered per browser
 *   ?client=all|<id>         client in the switcher; default is the page's data-client
 *   ?sidebar=collapsed       icon-only sidebar (Ctrl+B)
 *   ?menu=client|user        opens the client switcher or the operator menu
 *   ?dialog=<name>           opens <template id="dialog-<name>">; "command" and "shortcuts" are global
 *   ?state=<name>            shows [data-state~="<name>"] blocks and hides [data-state-default]
 */
(function () {
  const params = new URLSearchParams(location.search);
  const body = document.body;
  const store = {
    get(k) { try { return localStorage.getItem(k); } catch { return null; } },
    set(k, v) { try { localStorage.setItem(k, v); } catch { /* storage blocked */ } },
  };

  // ── Theme ──────────────────────────────────────────────
  const theme = params.get('theme') || store.get('mockups.theme');
  document.documentElement.classList.toggle('dark', theme === 'dark');
  window.toggleTheme = function () {
    const dark = document.documentElement.classList.toggle('dark');
    store.set('mockups.theme', dark ? 'dark' : 'light');
  };

  // ── Logo candidates ────────────────────────────────────
  // Each is drawn in --primary / --primary-foreground so it inverts with the theme.
  const P = 'style="fill:var(--primary)"';
  const F = 'style="fill:var(--primary-foreground)"';
  const S = 'style="stroke:var(--primary-foreground)"';
  const LOGOS = {
    // A · Page and block: the first mark, simplified. No tilt, no dashes, so it survives 16px.
    a: `<svg viewBox="0 0 32 32" fill="none" class="brand-logo" aria-hidden="true">
          <rect width="32" height="32" rx="8" ${P}/>
          <g ${S} stroke-width="1.75" stroke-linecap="round" stroke-linejoin="round">
            <path d="M10 6.5h7.5L22 11v6.5M14.5 25.5H10v-19" />
            <path d="M17.5 6.5V11H22" />
            <path d="M13 14.5h5" />
          </g>
          <rect x="16.5" y="18.5" width="9" height="9" rx="2" ${F}/>
        </svg>`,
    // B · Blocks: a 2×2 of blocks, one of them a page. The family mark: other blocks swap the page.
    b: `<svg viewBox="0 0 32 32" fill="none" class="brand-logo" aria-hidden="true">
          <rect width="32" height="32" rx="8" ${P}/>
          <rect x="7.5" y="7.5" width="7.5" height="7.5" rx="1.75" ${F}/>
          <rect x="7.5" y="17" width="7.5" height="7.5" rx="1.75" ${F}/>
          <rect x="17" y="17" width="7.5" height="7.5" rx="1.75" ${F}/>
          <path d="M17.75 8.25h3.6l2.4 2.4v3.6h-6z" ${S} stroke-width="1.5" stroke-linejoin="round" />
        </svg>`,
    // C · Folded page: one bold silhouette with a block cut into it. Reads best as a favicon.
    c: `<svg viewBox="0 0 32 32" fill="none" class="brand-logo" aria-hidden="true">
          <rect width="32" height="32" rx="8" ${P}/>
          <path d="M9.5 6h9l5 5v15h-14z" ${F}/>
          <path d="M18.5 6v5h5" ${P} style="fill:var(--primary);opacity:.35" />
          <rect x="12.5" y="16" width="8" height="6.5" rx="1.5" ${P}/>
        </svg>`,
  };
  const logoParam = params.get('logo');
  if (logoParam && LOGOS[logoParam]) store.set('mockups.logo', logoParam);
  // A is the chosen mark; B and C stay selectable for comparison.
  const logoKey = LOGOS[logoParam] ? logoParam : LOGOS[store.get('mockups.logo')] ? store.get('mockups.logo') : 'a';
  window.LOGOS = LOGOS;

  // ── Branding ───────────────────────────────────────────
  const LOGO_ACME = `
    <svg viewBox="0 0 32 32" class="brand-logo" aria-hidden="true">
      <rect width="32" height="32" rx="8" fill="#0f766e" />
      <path d="M9 23l7-14 7 14M11.8 18h8.4" fill="none" stroke="#ffffff" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" />
    </svg>`;
  const BRANDS = {
    default: { name: 'PDF Builder', logo: () => LOGOS[logoKey] },
    acme: { name: 'ACME Documents', logo: () => LOGO_ACME },
  };
  const brand = BRANDS[params.get('brand')] || BRANDS.default;
  window.brandLogo = () => brand.logo();
  window.brandName = () => brand.name;

  // ── Icons ──────────────────────────────────────────────
  const icon = (name, cls = 'icon') => `<i data-icon="${name}" class="${cls}"></i>`;
  function renderIcons(root) {
    root.querySelectorAll('i[data-icon]').forEach((el) => {
      const svg = (window.ICONS || {})[el.dataset.icon];
      if (!svg) { el.textContent = '?'; console.warn('unknown icon', el.dataset.icon); return; }
      const tpl = document.createElement('template');
      tpl.innerHTML = svg.trim();
      const node = tpl.content.firstElementChild;
      node.setAttribute('class', el.className || 'icon');
      if (el.getAttribute('style')) node.setAttribute('style', el.getAttribute('style'));
      node.setAttribute('aria-hidden', 'true');
      el.replaceWith(node);
    });
  }
  const kbd = (...keys) => `<span class="kbd-group">${keys.map((k) => (k === 'then' ? '<span class="then">then</span>' : `<kbd class="kbd">${k}</kbd>`)).join('')}</span>`;
  window.kbd = kbd;

  // ── Data ───────────────────────────────────────────────
  const CLIENTS = [
    { id: 'ecommerce', name: 'E-commerce' },
    { id: 'delivery', name: 'Delivery app' },
    { id: 'erp', name: 'ERP' },
  ];
  const clientParam = params.get('client');
  const client = clientParam === 'all' ? null : clientParam || body.dataset.client || null;
  window.MOCK = { client, clientName: CLIENTS.find((c) => c.id === client)?.name ?? null };

  const NAV = [
    { label: 'PDF', items: [
      { id: 'overview', label: 'Overview', icon: 'inbox', href: 'overview.html', keys: ['G', 'then', 'O'], badge: ['4', 'is-alert'] },
      { id: 'jobs', label: 'Jobs', icon: 'list', href: 'jobs.html', keys: ['G', 'then', 'J'] },
      { id: 'templates', label: 'Templates', icon: 'layout-template', href: 'templates.html', keys: ['G', 'then', 'T'] },
    ] },
    { label: 'Platform', items: [
      { id: 'keys', label: 'API keys', icon: 'key-round', href: 'api-keys.html', keys: ['G', 'then', 'K'] },
      { id: 'webhooks', label: 'Webhooks', icon: 'webhook', href: 'webhooks.html', keys: ['G', 'then', 'W'], badge: ['1', 'is-alert'] },
      { id: 'clients', label: 'Clients', icon: 'users', href: 'clients.html', keys: ['G', 'then', 'C'], badge: ['3', 'is-quiet'] },
    ] },
  ];

  // ── Shell ──────────────────────────────────────────────
  function renderShell() {
    const slot = document.querySelector('[data-shell]');
    if (!slot) return;
    const active = body.dataset.active;
    const page = NAV.flatMap((g) => g.items).find((i) => i.id === active);
    const collapsed = params.get('sidebar') === 'collapsed';
    const menu = params.get('menu');

    const groups = NAV.map((g) => `
      <div class="sb-group">
        <div class="sb-label">${g.label}</div>
        ${g.items.map((i) => `
          <a href="${i.href}" class="sb-item ${i.id === active ? 'is-active' : ''}" ${i.id === active ? 'aria-current="page"' : ''} title="${i.label}">
            ${icon(i.icon)}<span>${i.label}</span>
            ${i.badge ? `<span class="sb-badge ${i.badge[1]}">${i.badge[0]}</span>` : ''}
            <span class="kbd-hint">${kbd(...i.keys)}</span>
          </a>`).join('')}
      </div>`).join('');

    const clientMenu = menu === 'client' ? `
      <div class="menu" style="top:44px; left:0; right:0; min-width:0">
        <div class="menu-label">Client</div>
        <div class="menu-item ${client ? '' : 'is-active'}">${icon('users', 'icon muted')}All clients<span style="margin-left:auto">${kbd('Ctrl', '0')}</span></div>
        <div class="menu-sep"></div>
        ${CLIENTS.map((c, n) => `
          <div class="menu-item ${c.id === client ? 'is-active' : ''}">
            ${icon('building-2', 'icon muted')}${c.name}<span style="margin-left:auto">${kbd('Ctrl', String(n + 1))}</span>
          </div>`).join('')}
        <div class="menu-sep"></div>
        <a href="clients.html" class="menu-item">${icon('plus', 'icon muted')}Add client</a>
      </div>` : '';

    const userMenu = menu === 'user' ? `
      <div class="menu" style="bottom:56px; left:8px; right:8px; min-width:0">
        <div class="menu-label">Signed in with the admin key · session ends 22:40</div>
        <div class="menu-item">${icon('moon', 'icon muted')}Dark theme<span style="margin-left:auto">${kbd('Ctrl', 'Shift', 'L')}</span></div>
        <div class="menu-item">${icon('keyboard', 'icon muted')}Keyboard shortcuts<span style="margin-left:auto">${kbd('?')}</span></div>
        <div class="menu-sep"></div>
        <a class="menu-item" href="sign-in.html">${icon('log-out', 'icon muted')}Sign out</a>
      </div>` : '';

    const shell = document.createElement('div');
    shell.className = `app ${collapsed ? 'is-collapsed' : ''}`;
    shell.innerHTML = `
      <aside class="sidebar" aria-label="Console">
        <a href="overview.html" class="sb-block">
          ${brandLogo()}
          <span class="sb-title"><b>${brandName()}</b><span>Console · v1.4.0</span></span>
        </a>
        <div class="sb-switcher" aria-haspopup="menu" title="Client">
          <span class="sb-avatar">${icon(client ? 'building-2' : 'users', 'icon-sm')}</span>
          <span class="sb-title"><b>${MOCK.clientName ?? 'All clients'}</b><span>${client ? `<span class="mono" style="font-size:12px">${client}</span>` : '3 clients'}</span></span>
          ${icon('chevrons-up-down', 'icon-sm chev')}
          ${clientMenu}
        </div>
        <nav class="sb-content">${groups}</nav>
        <div class="sb-footer" style="position:relative">
          ${userMenu}
          <div class="sb-user">
            <span class="sb-initials">OP</span>
            <span class="sb-title"><b style="font-size:13px; font-weight:500">Operator</b><span>Admin key · ends 22:40</span></span>
            ${icon('chevrons-up-down', 'icon-sm muted')}
          </div>
        </div>
      </aside>
      <div class="inset">
        <header class="topbar">
          <button class="btn btn-ghost btn-icon-sm" title="Toggle sidebar (Ctrl+B)">${icon('panel-left')}</button>
          <span class="vsep"></span>
          <nav class="breadcrumb" aria-label="Breadcrumb">
            <span>${MOCK.clientName ?? 'All clients'}</span>
            ${icon('chevron-right', 'icon-xs')}
            <span class="current">${page?.label ?? document.title}</span>
          </nav>
          <a class="search-trigger" href="?dialog=command">
            ${icon('search', 'icon-sm')}Search or jump to…
            ${kbd('Ctrl', 'K')}
          </a>
          <button class="btn btn-ghost btn-icon-sm" onclick="toggleTheme()" title="Toggle theme (Ctrl+Shift+L)">${icon('moon')}</button>
        </header>
      </div>`;
    const main = document.querySelector('main');
    slot.replaceWith(shell);
    shell.querySelector('.inset').append(main);
    document.querySelectorAll('[data-eyebrow]').forEach((el) => el.remove());
  }

  // ── Global dialogs ─────────────────────────────────────
  const GLOBAL_DIALOGS = {
    command: () => `
      <div class="command" role="dialog" aria-label="Command menu">
        <div class="command-input">${icon('search', 'icon muted')}<span>inv</span><span class="muted" style="border-left:1.5px solid var(--foreground); height:18px"></span></div>
        <div class="command-list">
          <div class="command-heading">Templates</div>
          <div class="command-item is-active">${icon('layout-template')}invoice<span class="end muted small">Built in</span></div>
          <div class="command-item">${icon('layout-template')}tax-invoice<span class="end muted small">ERP</span></div>
          <div class="command-heading">Actions</div>
          <div class="command-item">${icon('plus')}New PDF with invoice<span class="end">${kbd('N')}</span></div>
          <div class="command-item">${icon('list')}Jobs using invoice</div>
          <div class="command-heading">Go to</div>
          <div class="command-item">${icon('inbox')}Overview${kbd('G', 'then', 'O')}</div>
          <div class="command-item">${icon('key-round')}API keys${kbd('G', 'then', 'K')}</div>
          <div class="command-heading">Client</div>
          <div class="command-item">${icon('building-2')}Switch to E-commerce${kbd('Ctrl', '1')}</div>
        </div>
        <div class="command-foot">
          <span>${kbd('↑')}${kbd('↓')} move</span><span>${kbd('↵')} open</span><span>${kbd('Esc')} close</span>
        </div>
      </div>`,
    shortcuts: () => {
      const row = (label, ...keys) => `<div class="shortcut-row"><span>${label}</span>${kbd(...keys)}</div>`;
      return `
      <div class="dialog is-xwide" role="dialog" aria-labelledby="sc-title" style="max-width:720px">
        <div class="row-between">
          <div><h2 class="dialog-title" id="sc-title">Keyboard shortcuts</h2><p class="dialog-desc">Single keys work anywhere except while typing in a field.</p></div>
          <button class="btn btn-ghost btn-icon-sm" aria-label="Close">${icon('x')}</button>
        </div>
        <div class="shortcut-grid">
          <div>
            <div class="command-heading" style="padding-left:0">General</div>
            ${row('Search or jump to', 'Ctrl', 'K')}
            ${row('Toggle sidebar', 'Ctrl', 'B')}
            ${row('Toggle theme', 'Ctrl', 'Shift', 'L')}
            ${row('Show shortcuts', '?')}
            <div class="command-heading" style="padding-left:0; margin-top:12px">Clients</div>
            ${row('All clients', 'Ctrl', '0')}
            ${row('First … ninth client', 'Ctrl', '1–9')}
          </div>
          <div>
            <div class="command-heading" style="padding-left:0">Go to</div>
            ${row('Overview', 'G', 'then', 'O')}
            ${row('Jobs', 'G', 'then', 'J')}
            ${row('Templates', 'G', 'then', 'T')}
            ${row('API keys', 'G', 'then', 'K')}
            <div class="command-heading" style="padding-left:0; margin-top:12px">Lists</div>
            ${row('Next / previous row', 'J', 'K')}
            ${row('Open row', '↵')}
            ${row('New (key, PDF, endpoint…)', 'N')}
            ${row('Filter', '/')}
          </div>
        </div>
      </div>`;
    },
  };

  // ── States and dialogs ─────────────────────────────────
  function renderState() {
    const state = params.get('state');
    if (!state) return;
    document.querySelectorAll('[data-state-default]').forEach((el) => { el.hidden = true; });
    document.querySelectorAll('[data-state]').forEach((el) => { el.hidden = !el.dataset.state.split(' ').includes(state); });
  }
  function renderDialog() {
    const name = params.get('dialog');
    if (!name) return;
    const tpl = document.getElementById(`dialog-${name}`);
    const overlay = document.createElement('div');
    overlay.className = 'overlay';
    if (tpl) {
      const content = tpl.content.cloneNode(true);
      // Toasts float over the page; everything else is modal.
      if (content.firstElementChild?.classList.contains('toast')) { body.append(content); return; }
      overlay.append(content);
    } else if (GLOBAL_DIALOGS[name]) {
      if (name === 'command') overlay.classList.add('is-top');
      overlay.innerHTML = GLOBAL_DIALOGS[name]();
    } else {
      return;
    }
    body.append(overlay);
  }

  // ── Client-dependent text ──────────────────────────────
  function renderClientText() {
    document.querySelectorAll('[data-client-name]').forEach((el) => { el.textContent = MOCK.clientName ?? ''; });
    document.querySelectorAll('[data-client-id]').forEach((el) => { el.textContent = MOCK.client ?? ''; });
    // Only ever hides, so it composes with ?state= instead of undoing it.
    document.querySelectorAll('[data-when-client]').forEach((el) => { if (!client) el.hidden = true; });
    document.querySelectorAll('[data-when-all]').forEach((el) => { if (client) el.hidden = true; });
    document.querySelectorAll('[data-brand-logo]').forEach((el) => { el.innerHTML = brandLogo(); });
    document.querySelectorAll('[data-brand-name]').forEach((el) => { el.textContent = brandName(); });
    document.querySelectorAll('[data-kbd]').forEach((el) => { el.outerHTML = kbd(...el.dataset.kbd.split(' ')); });
  }

  renderShell();
  renderState();
  renderDialog();
  renderClientText();
  document.title = `${document.title} · ${brandName()}`;
  renderIcons(document);
})();
