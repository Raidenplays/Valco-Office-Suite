"use strict";
/* Favorites Center: Favorites (Places bookmarks), Feeds (subscriptions), History. */
const { PlacesUtils } = ChromeUtils.importESModule("resource://gre/modules/PlacesUtils.sys.mjs");
const { ValcoFeeds } = ChromeUtils.importESModule("resource://valconet/ValcoFeeds.sys.mjs");

const W = window.browsingContext.topChromeWindow;
const $ = id => document.getElementById(id);
const ICON = {
  folder: "chrome://valconet/skin/icons/folder.svg",
  folderOpen: "chrome://valconet/skin/icons/folder-open.svg",
  page: "chrome://valconet/skin/icons/page-generic.svg",
  feed: "chrome://valconet/skin/icons/feeds.svg",
  day: "chrome://valconet/skin/icons/history-day.svg",
};
const expanded = new Set(["toolbar_____"]);
let section = "favorites";

function row({ icon, label, title, folder, count, onopen, onmenu }) {
  const r = document.createElement("div");
  r.className = "row" + (folder ? " folder" : "");
  r.title = title || label;
  const img = document.createElement("img");
  img.src = icon;
  img.onerror = () => (img.src = ICON.page);
  const span = document.createElement("span");
  span.textContent = label || "(untitled)";
  r.append(img, span);
  if (count != null) {
    const c = document.createElement("span");
    c.className = "count";
    c.textContent = `(${count})`;
    r.append(c);
  }
  r.addEventListener("click", e => {
    for (const s of document.querySelectorAll(".row.sel")) s.classList.remove("sel");
    r.classList.add("sel");
    onopen?.(e);
  });
  r.addEventListener("auxclick", e => e.button === 1 && onopen?.(e));
  r.addEventListener("contextmenu", e => {
    e.preventDefault();
    if (onmenu) showMenu(e, onmenu());
  });
  return r;
}

function open(url, e) {
  const where = e && (e.button === 1 || e.ctrlKey) ? "tab" : e?.shiftKey ? "window" : "current";
  W.ValcoNetWindow.openURL(url, where);
  if (where === "current") W.ValcoNetWindow.closeFavoritesCenter(true);
}
const iconFor = url => (/^https?:/.test(url) ? "page-icon:" + url : url.startsWith("chrome://valconet") ? "chrome://valconet/skin/icons/valconet.svg" : ICON.page);

/* ------------------------------------------------------------- context menu */
function showMenu(e, items) {
  const m = $("menu");
  m.replaceChildren(...items.map(it => {
    if (it === "-") return document.createElement("hr");
    const d = document.createElement("div");
    d.textContent = it[0];
    d.addEventListener("click", () => { hideMenu(); it[1](); });
    return d;
  }));
  m.hidden = false;
  m.style.left = Math.min(e.clientX, innerWidth - m.offsetWidth - 4) + "px";
  m.style.top = Math.min(e.clientY, innerHeight - m.offsetHeight - 4) + "px";
}
function hideMenu() { $("menu").hidden = true; }
addEventListener("mousedown", e => { if (!$("menu").contains(e.target)) hideMenu(); });
addEventListener("keydown", e => {
  if (e.key === "Escape") {
    if (!$("menu").hidden) hideMenu();
    else W.ValcoNetWindow.closeFavoritesCenter(false, !document.body.classList.contains("pinned") || false);
  }
});

/* ---------------------------------------------------------------- favorites */
async function renderFavorites() {
  const root = await PlacesUtils.promiseBookmarksTree(PlacesUtils.bookmarks.rootGuid);
  const list = $("list");
  const frag = document.createDocumentFragment();
  const byGuid = Object.fromEntries((root.children || []).map(c => [c.guid, c]));
  const toolbar = byGuid[PlacesUtils.bookmarks.toolbarGuid];
  const menu = byGuid[PlacesUtils.bookmarks.menuGuid];
  const other = byGuid[PlacesUtils.bookmarks.unfiledGuid];
  if (toolbar) frag.append(renderNode({ ...toolbar, title: "Favorites Bar" }));
  for (const c of menu?.children || []) frag.append(renderNode(c));
  if (other?.children?.length) frag.append(renderNode({ ...other, title: "Other Favorites" }));
  list.replaceChildren(frag);
  if (!list.children.length) {
    list.innerHTML = '<div class="empty">You have no favorites yet. Click "Add to Favorites" to add the page you are viewing.</div>';
  }
}

function renderNode(node) {
  if (node.type === PlacesUtils.TYPE_X_MOZ_PLACE_SEPARATOR) {
    return document.createDocumentFragment();
  }
  if (node.type === PlacesUtils.TYPE_X_MOZ_PLACE_CONTAINER) {
    const wrap = document.createElement("div");
    const isOpen = expanded.has(node.guid);
    wrap.append(row({
      icon: isOpen ? ICON.folderOpen : ICON.folder,
      label: node.title,
      folder: true,
      onopen: () => {
        isOpen ? expanded.delete(node.guid) : expanded.add(node.guid);
        renderFavorites();
      },
      onmenu: () => [
        ["Open in Tab Group", () => {
          for (const c of node.children || []) if (c.uri) W.ValcoNetWindow.openURL(c.uri, "tab");
        }],
        "-",
        ["Rename", () => rename(node)],
        ...(isRoot(node) ? [] : [["Delete", () => PlacesUtils.bookmarks.remove(node.guid).then(renderFavorites)]]),
      ],
    }));
    if (isOpen) {
      const kids = document.createElement("div");
      kids.className = "children";
      for (const c of node.children || []) kids.append(renderNode(c));
      wrap.append(kids);
    }
    return wrap;
  }
  return row({
    icon: iconFor(node.uri),
    label: node.title || node.uri,
    title: `${node.title || ""}\n${node.uri}`,
    onopen: e => open(node.uri, e),
    onmenu: () => [
      ["Open", () => open(node.uri)],
      ["Open in New Tab", () => W.ValcoNetWindow.openURL(node.uri, "tab")],
      ["Open in New Window", () => W.ValcoNetWindow.openURL(node.uri, "window")],
      "-",
      ["Rename", () => rename(node)],
      ["Delete", () => PlacesUtils.bookmarks.remove(node.guid).then(renderFavorites)],
    ],
  });
}
const isRoot = n => [PlacesUtils.bookmarks.toolbarGuid, PlacesUtils.bookmarks.menuGuid, PlacesUtils.bookmarks.unfiledGuid].includes(n.guid);
async function rename(node) {
  const val = { value: node.title || "" };
  if (Services.prompt.prompt(W, "Rename", "Type a new name:", val, null, {})) {
    await PlacesUtils.bookmarks.update({ guid: node.guid, title: val.value });
    renderFavorites();
  }
}

/* -------------------------------------------------------------------- feeds */
function renderFeeds() {
  const list = $("list");
  const feeds = ValcoFeeds.list();
  if (!feeds.length) {
    list.innerHTML =
      '<div class="empty">You have not subscribed to any feeds.<br><br>When a page has feeds, the Feeds button in the command bar turns orange. Click it to view and subscribe.</div>';
    return;
  }
  list.replaceChildren(...feeds.map(f => row({
    icon: ICON.feed,
    label: f.title || f.url,
    title: f.url,
    onopen: e => open("chrome://valconet/content/feed.html?url=" + encodeURIComponent(f.url), e),
    onmenu: () => [
      ["Open", () => open("chrome://valconet/content/feed.html?url=" + encodeURIComponent(f.url))],
      ["Open in New Tab", () => W.ValcoNetWindow.openURL("chrome://valconet/content/feed.html?url=" + encodeURIComponent(f.url), "tab")],
      "-",
      ["Rename", () => {
        const v = { value: f.title };
        if (Services.prompt.prompt(W, "Rename Feed", "Type a new name:", v, null, {})) ValcoFeeds.rename(f.url, v.value);
      }],
      ["Unsubscribe", () => ValcoFeeds.unsubscribe(f.url)],
    ],
  })));
}
const feedObserver = () => section === "feeds" && renderFeeds();
Services.obs.addObserver(feedObserver, "valconet-feeds-changed");
addEventListener("unload", () => Services.obs.removeObserver(feedObserver, "valconet-feeds-changed"));

/* ------------------------------------------------------------------ history */
const DAY = 86400e6; // microseconds
async function query(sql, params = {}) {
  const db = await PlacesUtils.promiseDBConnection();
  const rows = await db.executeCached(sql, params);
  return rows.map(r => ({ url: r.getResultByName("url"), title: r.getResultByName("title"), last: r.getResultByName("last"), visits: r.getResultByName("visits") }));
}
const hostOf = url => {
  try { return new URL(url).host || url.split(":")[0]; } catch (ex) { return url; }
};
function pageRow(p) {
  return row({
    icon: iconFor(p.url),
    label: p.title || p.url,
    title: `${p.title || ""}\n${p.url}`,
    onopen: e => open(p.url, e),
    onmenu: () => [
      ["Open", () => open(p.url)],
      ["Open in New Tab", () => W.ValcoNetWindow.openURL(p.url, "tab")],
      "-",
      ["Add to Favorites...", () => W.ValcoNetWindow.openDialog("add-favorite", { url: p.url, title: p.title })],
      ["Delete", () => PlacesUtils.history.remove(p.url).then(renderHistory)],
    ],
  });
}
function group(label, icon, pages, key, bySite = true) {
  const wrap = document.createElement("div");
  const isOpen = expanded.has(key);
  wrap.append(row({
    icon: icon === ICON.folder && isOpen ? ICON.folderOpen : icon,
    label,
    folder: true,
    onopen: () => {
      isOpen ? expanded.delete(key) : expanded.add(key);
      renderHistory();
    },
    onmenu: () => [["Delete", async () => {
      await PlacesUtils.history.remove(pages.map(p => p.url));
      renderHistory();
    }]],
  }));
  if (isOpen) {
    const kids = document.createElement("div");
    kids.className = "children";
    if (bySite) {
      const sites = new Map();
      for (const p of pages) {
        const h = hostOf(p.url);
        if (!sites.has(h)) sites.set(h, []);
        sites.get(h).push(p);
      }
      for (const [h, ps] of sites) kids.append(group(h, ICON.folder, ps, key + "|" + h, false));
    } else {
      for (const p of pages) kids.append(pageRow(p));
    }
    wrap.append(kids);
  }
  return wrap;
}

async function renderHistory() {
  const view = $("history-view").value;
  $("history-search").hidden = view !== "search";
  const list = $("list");
  const nowUs = Date.now() * 1000;
  const midnight = new Date();
  midnight.setHours(0, 0, 0, 0);
  const todayUs = midnight.getTime() * 1000;
  const base = `SELECT p.url, p.title, MAX(v.visit_date) AS last, p.visit_count AS visits
    FROM moz_historyvisits v JOIN moz_places p ON p.id = v.place_id
    WHERE p.hidden = 0 AND v.visit_date >= :since`;
  let frag = document.createDocumentFragment();

  if (view === "date") {
    const pages = await query(base + " GROUP BY p.id ORDER BY last DESC LIMIT 3000", { since: nowUs - 35 * DAY });
    const dow = (midnight.getDay() + 6) % 7; // Monday = 0
    const weekStart = todayUs - dow * DAY;
    const buckets = [
      ["Today", todayUs, Infinity],
      ...Array.from({ length: dow }, (_, i) => {
        const start = todayUs - (i + 1) * DAY;
        return [new Date(start / 1000).toLocaleDateString(undefined, { weekday: "long" }), start, start + DAY];
      }),
      ["Last Week", weekStart - 7 * DAY, weekStart],
      ["2 Weeks Ago", weekStart - 14 * DAY, weekStart - 7 * DAY],
      ["3 Weeks Ago", weekStart - 21 * DAY, weekStart - 14 * DAY],
    ];
    for (const [label, from, to] of buckets) {
      const ps = pages.filter(p => p.last >= from && p.last < to);
      if (ps.length) frag.append(group(label, ICON.day, ps, "d:" + label));
    }
  } else if (view === "site") {
    const pages = await query(base + " GROUP BY p.id ORDER BY last DESC LIMIT 3000", { since: nowUs - 35 * DAY });
    const sites = new Map();
    for (const p of pages) {
      const h = hostOf(p.url);
      if (!sites.has(h)) sites.set(h, []);
      sites.get(h).push(p);
    }
    for (const h of [...sites.keys()].sort()) frag.append(group(h, ICON.folder, sites.get(h), "s:" + h, false));
  } else if (view === "most") {
    const pages = await query(base + " GROUP BY p.id ORDER BY visits DESC, last DESC LIMIT 100", { since: nowUs - 90 * DAY });
    for (const p of pages) frag.append(pageRow(p));
  } else if (view === "today") {
    const pages = await query(base + " GROUP BY p.id ORDER BY last DESC LIMIT 500", { since: todayUs });
    for (const p of pages) frag.append(pageRow(p));
  } else if (view === "search") {
    const q = $("history-search").value.trim();
    if (q) {
      const pages = await query(
        base + " AND (p.url LIKE :q ESCAPE '\\' OR p.title LIKE :q ESCAPE '\\') GROUP BY p.id ORDER BY last DESC LIMIT 300",
        { since: 0, q: "%" + q.replace(/[\\%_]/g, m => "\\" + m) + "%" }
      );
      for (const p of pages) frag.append(pageRow(p));
      if (!pages.length) frag = Object.assign(document.createElement("div"), { className: "empty", textContent: `No pages match "${q}".` });
    } else {
      frag = Object.assign(document.createElement("div"), { className: "empty", textContent: "Type a word or address to search your history." });
    }
  }
  list.replaceChildren(frag);
  if (!list.children.length) {
    list.innerHTML = '<div class="empty">There is no history to show.</div>';
  }
}
$("history-view").addEventListener("change", () => {
  renderHistory();
  if ($("history-view").value === "search") $("history-search").focus();
});
let searchTimer;
$("history-search").addEventListener("input", () => {
  clearTimeout(searchTimer);
  searchTimer = setTimeout(renderHistory, 200);
});

/* ------------------------------------------------------------------ chrome */
for (const b of document.querySelectorAll("#sections button")) {
  b.addEventListener("click", () => FavCenter.show(b.dataset.section));
}
$("pin").addEventListener("click", () => W.ValcoNetWindow.setFavoritesPinned(true));
$("close").addEventListener("click", () => W.ValcoNetWindow.closeFavoritesCenter(false, true));
$("add").addEventListener("click", e => {
  $("add").setAttribute("open", "true");
  showMenu({ clientX: $("add").getBoundingClientRect().left, clientY: $("add").getBoundingClientRect().bottom }, [
    ["Add to Favorites...", () => W.ValcoNetWindow.addFavorite()],
    ["Add Current Tabs to Favorites...", () => W.PlacesCommandHook.bookmarkTabs?.()],
    ["Add to Favorites Bar", () => W.ValcoNetWindow.addToFavoritesBar()],
    "-",
    ["Import and Export...", () => W.PlacesCommandHook.showPlacesOrganizer("AllBookmarks")],
    ["Organize Favorites...", () => W.PlacesCommandHook.showPlacesOrganizer("AllBookmarks")],
  ]);
  setTimeout(() => $("add").removeAttribute("open"), 300);
  e.stopPropagation();
});

// Flyout closes when focus leaves it (clicking the page), like IE's.
addEventListener("blur", () => {
  setTimeout(() => {
    if (!document.hasFocus() && !document.body.classList.contains("pinned")) {
      const active = W.document.activeElement;
      if (active?.id !== "valco-favorites-button") W.ValcoNetWindow.closeFavoritesCenter(true);
    }
  }, 150);
});

// Keep the bookmark tree current.
const bmListener = () => section === "favorites" && renderFavorites();
const events = ["bookmark-added", "bookmark-removed", "bookmark-moved", "bookmark-title-changed", "bookmark-url-changed"];
PlacesUtils.observers.addListener(events, bmListener);
addEventListener("unload", () => PlacesUtils.observers.removeListener(events, bmListener));

var FavCenter = {
  show(name) {
    section = name || section;
    W.ValcoNetWindow.pendingFavSection = section;
    for (const b of document.querySelectorAll("#sections button")) {
      b.setAttribute("aria-selected", b.dataset.section === section);
    }
    $("history-bar").hidden = section !== "history";
    if (section === "favorites") renderFavorites();
    else if (section === "feeds") renderFeeds();
    else {
      expanded.add("d:Today");
      renderHistory();
    }
  },
  setPinned(p) {
    document.body.classList.toggle("pinned", p);
  },
};
FavCenter.setPinned(W.document.getElementById("valco-favcenter")?.hasAttribute("pinned"));
FavCenter.show(W.ValcoNetWindow.pendingFavSection || "favorites");
focus();
