"use strict";
const { ValcoFeeds } = ChromeUtils.importESModule("resource://valconet/ValcoFeeds.sys.mjs");
const $ = id => document.getElementById(id);
const url = new URLSearchParams(location.search).get("url") || "";
let feed = null, sort = "date", cat = null;

function render() {
  const q = $("filter").value.trim().toLowerCase();
  let items = feed.items.filter(it =>
    (!cat || it.categories.includes(cat)) &&
    (!q || (it.title + " " + it.summary + " " + it.author).toLowerCase().includes(q)));
  items = items.slice().sort((a, b) =>
    sort === "date" ? (b.date || 0) - (a.date || 0) :
    sort === "title" ? a.title.localeCompare(b.title) :
    (a.author || "").localeCompare(b.author || ""));
  $("count").textContent = `Displaying ${items.length} / ${feed.items.length}`;
  const main = $("items");
  main.replaceChildren(...items.map(it => {
    const art = document.createElement("article");
    const h3 = document.createElement("h3");
    const a = document.createElement(it.link ? "a" : "span");
    if (it.link) a.href = it.link;
    a.textContent = it.title;
    h3.append(a);
    const meta = document.createElement("div");
    meta.className = "meta";
    const arrow = document.createElement("span");
    arrow.className = "arrow";
    const parts = [];
    if (it.date) parts.push(it.date.toLocaleString(undefined, { dateStyle: "long", timeStyle: "short" }));
    if (it.author) parts.push(it.author);
    meta.append(arrow, document.createTextNode(parts.join(" · ")));
    const p = document.createElement("div");
    p.className = "summary";
    p.textContent = it.summary;
    art.append(h3, meta, p);
    return art;
  }));
}

function updateBox() {
  const subbed = ValcoFeeds.isSubscribed(url);
  $("box").classList.toggle("subscribed", subbed);
  $("box-text").textContent = subbed
    ? "You've successfully subscribed to this feed! Updated content will appear in the Feeds list of the Favorites Center and on ValcoNet Home."
    : "You are viewing a feed that contains frequently updated content. When you subscribe to a feed, it is added to the Feeds list in the Favorites Center and its headlines appear on ValcoNet Home.";
  $("subscribe").textContent = subbed ? "Unsubscribe" : "Subscribe to this feed";
}

$("subscribe").addEventListener("click", e => {
  e.preventDefault();
  if (ValcoFeeds.isSubscribed(url)) ValcoFeeds.unsubscribe(url);
  else ValcoFeeds.subscribe(url, feed?.title);
  updateBox();
});
$("filter").addEventListener("input", () => feed && render());
for (const a of document.querySelectorAll("#sort a")) {
  a.addEventListener("click", () => {
    sort = a.dataset.sort;
    for (const b of document.querySelectorAll("#sort a")) {
      b.classList.toggle("on", b === a);
      b.textContent = (b === a ? "▾ " : "") + b.dataset.sort[0].toUpperCase() + b.dataset.sort.slice(1);
    }
    render();
  });
}

(async () => {
  if (!/^(https?:\/\/|file:\/\/\/)/.test(url)) {
    $("title").textContent = "Feed not available";
    $("error").hidden = false;
    $("error").textContent = "ValcoNet can only show http://, https:// and file:/// feeds.";
    $("box").hidden = true;
    return;
  }
  updateBox();
  try {
    feed = await ValcoFeeds.fetch(url, window);
    document.title = feed.title;
    $("title").textContent = feed.title;
    $("desc").textContent = feed.description;
    const cats = [...new Set(feed.items.flatMap(i => i.categories).filter(Boolean))].slice(0, 30);
    if (cats.length) {
      const box = $("cats");
      box.hidden = false;
      const all = document.createElement("a");
      all.textContent = "All";
      all.className = "on";
      box.append(all);
      for (const c of [null, ...cats].slice(1)) {
        const a = document.createElement("a");
        a.textContent = c;
        box.append(a);
      }
      for (const a of box.querySelectorAll("a")) {
        a.addEventListener("click", () => {
          cat = a === all ? null : a.textContent;
          for (const b of box.querySelectorAll("a")) b.classList.toggle("on", b === a);
          render();
        });
      }
    }
    render();
  } catch (ex) {
    $("title").textContent = "ValcoNet cannot display this feed";
    $("error").hidden = false;
    $("error").textContent = String(ex.message || ex);
  }
})();
