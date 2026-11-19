"use strict";
const { PlacesUtils } = ChromeUtils.importESModule("resource://gre/modules/PlacesUtils.sys.mjs");
const url = DialogArgs.url || "";
$("name").value = DialogArgs.title || url;
$("name").select();
$("cancel").onclick = () => window.close();

async function fillFolders(selectGuid) {
  const root = await PlacesUtils.promiseBookmarksTree(PlacesUtils.bookmarks.rootGuid);
  const sel = $("folder");
  sel.replaceChildren();
  const add = (node, depth, label) => {
    const o = document.createElement("option");
    o.value = node.guid;
    o.textContent = "   ".repeat(depth) + (label || node.title || "(untitled)");
    sel.append(o);
    for (const c of node.children || []) {
      if (c.type === PlacesUtils.TYPE_X_MOZ_PLACE_CONTAINER) add(c, depth + 1);
    }
  };
  const kids = Object.fromEntries((root.children || []).map(c => [c.guid, c]));
  add(kids[PlacesUtils.bookmarks.menuGuid] || { guid: PlacesUtils.bookmarks.menuGuid }, 0, "Favorites");
  add(kids[PlacesUtils.bookmarks.toolbarGuid] || { guid: PlacesUtils.bookmarks.toolbarGuid }, 1, "Favorites Bar");
  add(kids[PlacesUtils.bookmarks.unfiledGuid] || { guid: PlacesUtils.bookmarks.unfiledGuid }, 1, "Other Favorites");
  sel.value = selectGuid || Services.prefs.getStringPref("valconet.addFavorite.lastFolder", PlacesUtils.bookmarks.menuGuid);
  if (sel.selectedIndex < 0) sel.selectedIndex = 0;
}
fillFolders();

$("newfolder").onclick = async () => {
  const v = { value: "New Folder" };
  if (!Services.prompt.prompt(window, "Create a Folder", "Folder Name:", v, null, {}) || !v.value.trim()) return;
  const f = await PlacesUtils.bookmarks.insert({ parentGuid: $("folder").value, type: PlacesUtils.bookmarks.TYPE_FOLDER, title: v.value.trim() });
  await fillFolders(f.guid);
};

$("add").onclick = async () => {
  if (!/^(https?|file|chrome|about|ftp):/.test(url)) {
    Services.prompt.alert(window, "Add a Favorite", "This page can't be added to Favorites.");
    return;
  }
  Services.prefs.setStringPref("valconet.addFavorite.lastFolder", $("folder").value);
  await PlacesUtils.bookmarks.insert({ parentGuid: $("folder").value, url, title: $("name").value.trim() || url });
  window.close();
};
addEventListener("keydown", e => e.key === "Enter" && e.target.id === "name" && $("add").click());
