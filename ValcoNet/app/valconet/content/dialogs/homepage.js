"use strict";
const win = DialogArgs.opener || Services.wm.getMostRecentWindow("navigator:browser");
const current = win.gBrowser.currentURI.spec;
const tabUrls = win.gBrowser.visibleTabs.map(t => t.linkedBrowser.currentURI.spec).filter(u => !u.startsWith("about:"));
$("url").textContent = current;
$("no").onclick = () => window.close();
$("yes").onclick = () => {
  const mode = document.querySelector("input[name=mode]:checked").value;
  const existing = Prefs.get("browser.startup.homepage", "").split("|").filter(u => u && u !== "about:blank");
  const pages = mode === "only" ? [current] : mode === "add" ? [...new Set([...existing, current])] : tabUrls;
  Prefs.set("browser.startup.homepage", (pages.length ? pages : [current]).join("|"));
  window.close();
};
