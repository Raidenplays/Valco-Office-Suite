"use strict";
const { SearchService } = ChromeUtils.importESModule("moz-src:///toolkit/components/search/SearchService.sys.mjs");
const { PlacesUtils } = ChromeUtils.importESModule("resource://gre/modules/PlacesUtils.sys.mjs");
const { ValcoFeeds } = ChromeUtils.importESModule("resource://valconet/ValcoFeeds.sys.mjs");
const { PrivateBrowsingUtils } = ChromeUtils.importESModule("resource://gre/modules/PrivateBrowsingUtils.sys.mjs");

const isPrivate = PrivateBrowsingUtils.isContentWindowPrivate(window);
document.getElementById("date").textContent = new Date().toLocaleDateString(undefined, {
  weekday: "long", year: "numeric", month: "long", day: "numeric",
});

async function engine() {
  return isPrivate ? SearchService.getDefaultPrivate() : SearchService.getDefault();
}
engine().then(e => {
  document.getElementById("engine").textContent = `Searching with ${e.name}`;
  document.getElementById("q").placeholder = `Search ${e.name}`;
});
document.getElementById("search").addEventListener("submit", async ev => {
  ev.preventDefault();
  const q = document.getElementById("q").value.trim();
  if (!q) return;
  const e = await engine();
  location.href = e.getSubmission(q).uri.spec;
});

async function renderTiles() {
  const tree = await PlacesUtils.promiseBookmarksTree(PlacesUtils.bookmarks.toolbarGuid);
  const tiles = document.getElementById("tiles");
  tiles.replaceChildren();
  for (const b of (tree.children || []).filter(c => c.uri && /^(https?|chrome):/.test(c.uri)).slice(0, 16)) {
    const a = document.createElement("a");
    a.className = "tile";
    a.href = b.uri;
    a.title = b.uri;
    const img = document.createElement("img");
    img.src = "page-icon:" + b.uri;
    img.onerror = () => (img.src = "chrome://valconet/skin/icons/page-generic.svg");
    const span = document.createElement("span");
    span.textContent = b.title || b.uri;
    a.append(img, span);
    tiles.append(a);
  }
  if (!tiles.children.length) {
    const p = document.createElement("p");
    p.style.gridColumn = "1 / -1";
    p.textContent = "Add sites to your Favorites Bar with the green ★ button and they'll show up here.";
    tiles.append(p);
  }
}
const tilesListener = () => renderTiles();
PlacesUtils.observers.addListener(["bookmark-added", "bookmark-removed", "bookmark-title-changed"], tilesListener);
addEventListener("unload", () => PlacesUtils.observers.removeListener(["bookmark-added", "bookmark-removed", "bookmark-title-changed"], tilesListener));

(async () => {
  await renderTiles();

  // Headlines from subscribed feeds (text only).
  const feeds = Services.prefs.getBoolPref("valconet.home.feeds", true) ? ValcoFeeds.list().slice(0, 4) : [];
  if (!feeds.length) return;
  const items = [];
  await Promise.all(feeds.map(async f => {
    try {
      const data = await ValcoFeeds.fetch(f.url, window);
      for (const it of data.items.slice(0, 5)) items.push({ ...it, src: f.title || data.title });
    } catch (ex) {}
  }));
  items.sort((a, b) => (b.date || 0) - (a.date || 0));
  const ul = document.getElementById("news");
  for (const it of items.slice(0, 10)) {
    const li = document.createElement("li");
    const a = document.createElement(it.link ? "a" : "span");
    if (it.link) a.href = it.link;
    a.textContent = it.title;
    const s = document.createElement("span");
    s.className = "src";
    s.textContent = " — " + it.src;
    li.append(a, s);
    ul.append(li);
  }
  document.getElementById("news-section").hidden = !items.length;
})();
