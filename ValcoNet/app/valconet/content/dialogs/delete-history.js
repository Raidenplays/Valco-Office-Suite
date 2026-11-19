"use strict";
const { Sanitizer } = ChromeUtils.importESModule("resource:///modules/Sanitizer.sys.mjs");
const { PlacesUtils } = ChromeUtils.importESModule("resource://gre/modules/PlacesUtils.sys.mjs");
const KEYS = ["preserve", "cache", "cookies", "history", "downloads", "formdata", "passwords", "siteSettings"];
for (const k of KEYS) {
  $(k).checked = Prefs.get("valconet.deleteHistory." + k, $(k).checked);
}
$("cancel").onclick = () => window.close();
$("about").onclick = e => {
  e.preventDefault();
  DialogArgs.opener?.ValcoNetWindow.openURL("https://support.mozilla.org/kb/delete-browsing-search-download-history-firefox", "tab");
};

async function favoriteHosts() {
  const hosts = new Set();
  const walk = node => {
    if (node.uri) {
      try { hosts.add(new URL(node.uri).host); } catch (ex) {}
    }
    (node.children || []).forEach(walk);
  };
  walk(await PlacesUtils.promiseBookmarksTree(PlacesUtils.bookmarks.rootGuid));
  hosts.delete("");
  return hosts;
}

$("delete").onclick = async () => {
  for (const k of KEYS) Prefs.set("valconet.deleteHistory." + k, $(k).checked);
  $("delete").disabled = $("cancel").disabled = true;
  $("busy").hidden = false;
  try {
    const items = ["cache", "history", "downloads", "formdata", "siteSettings"].filter(k => $(k).checked);
    if (items.includes("history")) items.push("sessions");
    if ($("cookies").checked) {
      if ($("preserve").checked) {
        // Clear site data host by host, skipping sites in Favorites.
        const keep = await favoriteHosts();
        const hosts = new Set();
        for (const c of Services.cookies.cookies) hosts.add(c.rawHost);
        const all = [...hosts].filter(h => ![...keep].some(k => k === h || k.endsWith("." + h) || h.endsWith("." + k)));
        await Promise.all(all.map(host => new Promise(resolve =>
          Services.clearData.deleteDataFromSite(host, {}, true,
            Ci.nsIClearDataService.CLEAR_COOKIES | Ci.nsIClearDataService.CLEAR_DOM_STORAGES, { onDataDeleted: resolve }))));
      } else {
        items.push("cookies", "offlineApps");
      }
    }
    if (items.length) await Sanitizer.sanitize(items, { ignoreTimespan: true });
    if ($("passwords").checked) Services.logins.removeAllUserFacingLogins();
  } catch (ex) {
    console.error("ValcoNet: delete history", ex);
  }
  window.close();
};
