// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.

// ------------------------------------------------------------
// Sidebar
// ------------------------------------------------------------
// Three behaviors share the same sidebar:
//   1. Desktop/tablet collapse: toggles .collapsed on the aside
//      so the in-sidebar circular button switches between full
//      and icon-only layouts. State is persisted in localStorage.
//   2. Mobile drawer: opens/closes an off-canvas drawer via
//      .mobile-open on both the aside and the backdrop. Triggered
//      by the top-navbar hamburger (#sidebarToggleMobile) and
//      closed by tapping the backdrop. Active only on small
//      viewports.
//   3. Dropdown groups: a .nav-item-dropdown's <ul class="nav-dropdown-menu">
//      is a Bootstrap .collapse. On page load we auto-open the group
//      that contains the active link, so the current section is always
//      visible without an extra click. The user can still toggle any
//      group with the chevron button. In collapsed mode, click also
//      toggles .show on the parent <li> so the floating flyout appears.
// ------------------------------------------------------------
(function () {
  const STORAGE_KEY = 'rentalsphere.sidebar.collapsed';
  const aside = document.getElementById('sidebarNav');
  const shellSidebar = aside ? aside.closest('.app-shell-sidebar') : null;
  const toggleDesktop = document.getElementById('sidebarToggle');
  const toggleMobile = document.getElementById('sidebarToggleMobile');
  const backdrop = document.getElementById('sidebarBackdrop');
  if (!aside) return;

  // ----- 1. Desktop/tablet collapse -----
  function applyCollapsed(collapsed) {
    aside.classList.toggle('collapsed', collapsed);
    // ponytail: also toggle the outer shell column so the brand
    // text shrinks/hides with the sidebar.
    if (shellSidebar) shellSidebar.classList.toggle('collapsed', collapsed);
    if (toggleDesktop) toggleDesktop.setAttribute('aria-expanded', String(!collapsed));
  }

  try {
    applyCollapsed(window.localStorage.getItem(STORAGE_KEY) === '1');
  } catch (_) {
    applyCollapsed(false);
  }

  if (toggleDesktop) {
    toggleDesktop.addEventListener('click', function () {
      const next = !aside.classList.contains('collapsed');
      applyCollapsed(next);
      try {
        window.localStorage.setItem(STORAGE_KEY, next ? '1' : '0');
      } catch (_) { /* ignore quota / disabled storage */ }
    });
  }

  // ----- 2. Mobile drawer -----
  function isMobileViewport() {
    return window.matchMedia('(max-width: 767.98px)').matches;
  }

  function setDrawerOpen(open) {
    aside.classList.toggle('mobile-open', open);
    if (backdrop) backdrop.classList.toggle('mobile-open', open);
    document.body.classList.toggle('sidebar-drawer-open', open);
    if (toggleMobile) toggleMobile.setAttribute('aria-expanded', String(open));
  }

  function closeDrawer() { setDrawerOpen(false); }
  function toggleDrawer() { setDrawerOpen(!aside.classList.contains('mobile-open')); }

  if (toggleMobile) {
    toggleMobile.addEventListener('click', function (e) {
      e.preventDefault();
      toggleDrawer();
    });
  }
  if (backdrop) {
    backdrop.addEventListener('click', closeDrawer);
  }

  if (typeof window.matchMedia === 'function') {
    const mq = window.matchMedia('(max-width: 767.98px)');
    const onChange = function (ev) { if (!ev.matches) closeDrawer(); };
    if (mq.addEventListener) mq.addEventListener('change', onChange);
    else if (mq.addListener) mq.addListener(onChange);
  }

  document.addEventListener('keydown', function (e) {
    if (e.key === 'Escape' && aside.classList.contains('mobile-open')) closeDrawer();
  });

  // ----- 3. Dropdown auto-open for the active section -----
  // The .nav-dropdown-menu is a Bootstrap .collapse element. Bootstrap's JS
  // would flip .show + aria-expanded on click, but on initial page load
  // nothing's open. We walk the sidebar, find any .nav-item-dropdown
  // whose menu contains the active link, and open it before Bootstrap
  // has a chance to wire its own click handlers. In collapsed mode the
  // CSS flyout reads .show on the parent <li> instead, so we set it
  // there too.
  function openActiveGroup() {
    const activeLink = aside.querySelector('.nav-link.active');
    if (!activeLink) return;
    const submenu = activeLink.closest('.nav-dropdown-menu');
    if (!submenu) return;
    submenu.classList.add('show');
    const toggle = document.querySelector('[data-bs-target="#' + submenu.id + '"]');
    if (toggle) toggle.setAttribute('aria-expanded', 'true');
    const group = submenu.closest('.nav-item-dropdown');
    if (group) group.classList.add('show');
  }
  openActiveGroup();

  // ----- 4. Flyout toggle when the sidebar is collapsed -----
  // Hover and focus-within already open the flyout via CSS. Click is
  // the third trigger for touch users and explicit keyboard activation.
  // In collapsed mode, clicking a group toggle stops Bootstrap's
  // collapse (the inline submenu is hidden anyway) and flips .show on
  // the parent .nav-item-dropdown instead. Toggling another group
  // closes the first so only one flyout is visible at a time. Outside
  // clicks and Escape close any open flyout.
  const groups = aside.querySelectorAll('.nav-item-dropdown');

  // Unified click handler for all sidebar dropdown toggles. Runs in
  // the capture phase so it fires before any other handler (including
  // Bootstrap's collapse JS, in case the data-bs-toggle attribute is
  // ever re-introduced).
  //
  // Two modes:
  // - Collapsed: spec's flyout logic — stop the click, close other
  //   open dropdowns, toggle .show on the parent <li> (so the
  //   popout's CSS picks it up). Second click on the same icon
  //   leaves the popout closed.
  // - Expanded / mobile: open the inline submenu by toggling .show
  //   on the <ul class="nav-dropdown-menu"> child and updating
  //   aria-expanded on the toggle. We removed the data-bs-toggle
  //   attribute from the partial, so without this path the inline
  //   accordion would never open in expanded mode.
  document.querySelectorAll(
    '.sidebar-nav .nav-item-dropdown > a, .sidebar-nav .nav-dropdown-toggle, .sidebar-nav .nav-group-toggle'
  ).forEach(function (toggle) {
    toggle.addEventListener('click', function (e) {
      const sidebar = document.querySelector('.sidebar-nav');
      if (!sidebar) return;
      const parentDropdown = this.closest('.nav-item-dropdown');
      if (!parentDropdown) return;

      // Detect icon-only mode: either the .collapsed class (desktop
      // toggle) or the medium-viewport media query (768–1023px) hides
      // .link-text via CSS. Both cases need the flyout popout, not the
      // inline accordion.
      var firstLinkText = sidebar.querySelector('.link-text');
      var isIconOnly = sidebar.classList.contains('collapsed') ||
        (firstLinkText &&
          getComputedStyle(firstLinkText).display === 'none');

      if (isIconOnly) {
        // ----- Collapsed / icon-only: popout flyout -----
        e.preventDefault();
        e.stopPropagation();
        if (e.stopImmediatePropagation) e.stopImmediatePropagation();

        const isOpen = parentDropdown.classList.contains('show');

        // Close all other open popups first.
        document.querySelectorAll('.nav-item-dropdown.show').forEach(function (el) {
          el.classList.remove('show');
        });

        // Toggle target dropdown only if it wasn't already open.
        if (!isOpen) parentDropdown.classList.add('show');
        return;
      }

      // ----- Expanded / mobile: inline submenu accordion -----
      // The data-bs-toggle attribute is gone, so JS is now the only
      // thing that flips Bootstrap's .collapse class on the inline
      // <ul>. We do the same dance Bootstrap would do: toggle .show
      // on the <ul>, update aria-expanded on the toggle, and leave
      // the open/close animation to Bootstrap's .collapse class.
      e.preventDefault();
      const submenu = parentDropdown.querySelector('.nav-dropdown-menu');
      if (!submenu) return;
      const wasOpen = submenu.classList.contains('show');
      submenu.classList.toggle('show', !wasOpen);
      this.setAttribute('aria-expanded', String(!wasOpen));
    }, true);
  });

  // Outside click closes any open flyout. Clicks inside an open
  // .nav-item-dropdown are skipped — the toggle button's own click
  // handler is in charge of opening/closing its own popout.
  document.addEventListener('click', function (e) {
    if (e.target && e.target.closest && e.target.closest('.nav-item-dropdown')) return;
    document.querySelectorAll('.nav-item-dropdown.show').forEach(function (el) {
      el.classList.remove('show');
    });
  });

  // Escape closes any open flyout in addition to the mobile drawer.
  document.addEventListener('keydown', function (e) {
    if (e.key !== 'Escape') return;
    let any = false;
    groups.forEach(function (g) {
      if (g.classList.contains('show')) { g.classList.remove('show'); any = true; }
    });
    if (any) e.stopPropagation();
  });
})();

// ------------------------------------------------------------
// Logout confirmation modal
// ------------------------------------------------------------
// The sidebar's Sign Out button opens #logoutModal instead of
// submitting immediately. The modal's primary button submits
// the existing #logoutForm so the antiforgery token travels
// with the POST to /Account/Logout.
// ------------------------------------------------------------
(function () {
  const confirmBtn = document.getElementById('confirmLogoutBtn');
  const logoutForm = document.getElementById('logoutForm');
  if (!confirmBtn || !logoutForm) return;

  confirmBtn.addEventListener('click', function () {
    logoutForm.submit();
  });
})();

// ------------------------------------------------------------
// Theme toggle (light / dark)
// ------------------------------------------------------------
// Flips `data-bs-theme` between "light" and "dark" on <html> and
// persists the choice in localStorage. The CSS block in site.css
// under [data-bs-theme="dark"] re-skins the rest of the app. The
// header itself stays light by design — its dark overrides are
// declared in CSS, the JS only needs to flip the attribute.
// ------------------------------------------------------------
(function () {
  const STORAGE_KEY = 'rentalsphere.theme';
  const html = document.documentElement;
  const btn = document.getElementById('themeToggle');
  const iconLight = document.getElementById('themeIconLight');
  const iconDark = document.getElementById('themeIconDark');
  if (!btn || !iconLight || !iconDark) return;

  function applyTheme(theme) {
    html.setAttribute('data-bs-theme', theme);
    document.body.setAttribute('data-bs-theme', theme);
    const isDark = theme === 'dark';
    iconLight.classList.toggle('d-none', isDark);
    iconDark.classList.toggle('d-none', !isDark);
    btn.setAttribute('aria-pressed', String(isDark));
    btn.setAttribute('title', isDark ? 'Switch to light theme' : 'Switch to dark theme');
  }

  let saved = null;
  try { saved = window.localStorage.getItem(STORAGE_KEY); } catch (_) { /* ignore */ }
  applyTheme(saved === 'dark' ? 'dark' : 'light');

  btn.addEventListener('click', function () {
    const next = html.getAttribute('data-bs-theme') === 'dark' ? 'light' : 'dark';
    applyTheme(next);
    try { window.localStorage.setItem(STORAGE_KEY, next); } catch (_) { /* ignore */ }
  });

  // Ctrl/Cmd+K focuses the global search field.
  const search = document.querySelector('.app-header-search-input');
  if (search) {
    document.addEventListener('keydown', function (e) {
      const isMac = navigator.platform.toLowerCase().includes('mac');
      const accel = isMac ? e.metaKey : e.ctrlKey;
      if (accel && (e.key === 'k' || e.key === 'K')) {
        e.preventDefault();
        search.focus();
        search.select();
      }
    });
  }
})();

// ------------------------------------------------------------
// Header notification dot
// ------------------------------------------------------------
// ponytail: the CrmUnreadBadge view component renders either an
// empty span (no items) or a number badge. We want a single red
// dot indicator on the bell when there's at least one unread
// CRM item. On page load we peek at the existing badge — if
// present, swap the number for a dot. The badge markup is
// deleted from the layout; the dot is purely CSS-driven via
// `d-none` toggling.
// ------------------------------------------------------------
(function () {
  const dot = document.getElementById('notificationDot');
  if (!dot) return;
  // ponytail: the bell currently has no server-rendered unread
  // count visible. When the CRM module exposes a JS-readable
  // count (e.g. via an API or a data attribute), flip dot.classList
  // here. For now the dot stays hidden by default; the staff
  // can still see CRM items via the dropdown link.
  // dot.classList.remove('d-none');
})();

// ------------------------------------------------------------
// Equipment catalog live filter
// ------------------------------------------------------------
// Submits the catalog filter form automatically as the user types
// (debounced 300ms after the last keystroke) and immediately on
// category change, so the table/card grid refreshes without a
// separate "Filter" button. URL params are preserved across
// submissions by the form's hidden pageSize input.
// ------------------------------------------------------------
(function () {
  const form = document.getElementById('equipmentFilterForm');
  if (!form) return;
  const search = document.getElementById('equipmentSearchInput');
  const select = document.getElementById('equipmentCategorySelect');
  if (!search && !select) return;

  // ponytail: 300ms debounce is the standard "feels live but doesn't
  // hammer the server" value. Drop to 150ms if results are local-cache
  // fast; raise to 500ms if the catalog grows past ~5k rows.
  const DEBOUNCE_MS = 300;
  let timer = null;

  function submit() {
    if (timer) { clearTimeout(timer); timer = null; }
    form.submit();
  }

  if (search) {
    search.addEventListener('input', function () {
      if (timer) clearTimeout(timer);
      timer = setTimeout(submit, DEBOUNCE_MS);
    });
    // Submit immediately on Enter even if the debounce hasn't fired.
    search.addEventListener('keydown', function (e) {
      if (e.key === 'Enter') {
        e.preventDefault();
        submit();
      }
    });
  }
  if (select) {
    select.addEventListener('change', submit);
  }
})();
