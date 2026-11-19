"use strict";
const { SearchService } = ChromeUtils.importESModule("moz-src:///toolkit/components/search/SearchService.sys.mjs");
const { PrivateBrowsingUtils } = ChromeUtils.importESModule("resource://gre/modules/PrivateBrowsingUtils.sys.mjs");
const { SessionStore } = ChromeUtils.importESModule("resource:///modules/sessionstore/SessionStore.sys.mjs");

const chromeWin = window.browsingContext.topChromeWindow;
const $ = id => document.getElementById(id);

if (PrivateBrowsingUtils.isContentWindowPrivate(window)) {
  document.body.className = "inprivate";
  $("normal").hidden = true;
  $("private").hidden = false;
  document.title = "InPrivate";
} else if (!Services.prefs.getBoolPref("valconet.newtab.show", true)) {
  // IE8's "Show this page" unchecked: a blank tab.
  document.body.replaceChildren();
  document.title = "Blank Page";
} else {
  // Recently closed tabs.
  const closed = $("closed");
  let data = [];
  try {
    data = SessionStore.getClosedTabDataForWindow(chromeWin) || [];
  } catch (ex) {}
  if (!data.length) {
    closed.innerHTML = '<li class="empty">You haven\'t closed any tabs yet.</li>';
  }
  data.slice(0, 10).forEach((tab, i) => {
    const li = document.createElement("li");
    const b = document.createElement("button");
    b.className = "vn-link-button";
    const img = document.createElement("img");
    img.src = tab.image || "chrome://valconet/skin/icons/page-generic.svg";
    img.onerror = () => (img.src = "chrome://valconet/skin/icons/page-generic.svg");
    const span = document.createElement("span");
    span.textContent = tab.title || "Untitled";
    b.append(img, span);
    b.addEventListener("click", () => {
      chromeWin.undoCloseTab(i);
      chromeWin.gBrowser.removeTab(chromeWin.gBrowser.getTabForBrowser(window.browsingContext.embedderElement));
    });
    li.append(b);
    closed.append(li);
  });

  const restore = $("restore");
  restore.disabled = !SessionStore.canRestoreLastSession;
  if (restore.disabled) restore.textContent = "No previous session to reopen";
  restore.addEventListener("click", () => SessionStore.restoreLastSession());

  $("inprivate").addEventListener("click", () => chromeWin.OpenBrowserWindow({ private: true }));

  $("show").addEventListener("change", e => {
    Services.prefs.setBoolPref("valconet.newtab.show", e.target.checked);
  });

  // Clipboard text, IE8's "Use an Accelerator" box.
  (async () => {
    let text = "";
    try {
      const trans = Cc["@mozilla.org/widget/transferable;1"].createInstance(Ci.nsITransferable);
      trans.init(null);
      trans.addDataFlavor("text/plain");
      Services.clipboard.getData(trans, Services.clipboard.kGlobalClipboard, window.browsingContext.currentWindowContext);
      const out = {};
      trans.getTransferData("text/plain", out);
      text = out.value.QueryInterface(Ci.nsISupportsString).data.trim().slice(0, 500);
    } catch (ex) {}
    const clip = $("clip"), actions = $("clip-actions");
    if (!text) {
      clip.textContent = "Copy some text, and options for it will appear here.";
      return;
    }
    clip.textContent = text;
    const add = (label, fn) => {
      const li = document.createElement("li");
      const b = document.createElement("button");
      b.className = "vn-link-button";
      b.textContent = label;
      b.addEventListener("click", fn);
      li.append(b);
      actions.append(li);
    };
    let url = null;
    try {
      url = Services.uriFixup.getFixupURIInfo(text, Services.uriFixup.FIXUP_FLAG_NONE).preferredURI;
    } catch (ex) {}
    if (url && /^https?$/.test(url.scheme) && !/\s/.test(text)) {
      add("Go to copied address", () => (location.href = url.spec));
    }
    const engine = await SearchService.getDefault();
    add(`Search with ${engine.name}`, () => (location.href = engine.getSubmission(text).uri.spec));
    add("Translate with Google Translate", () =>
      (location.href = "https://translate.google.com/?sl=auto&op=translate&text=" + encodeURIComponent(text)));
    add("Map with OpenStreetMap", () =>
      (location.href = "https://www.openstreetmap.org/search?query=" + encodeURIComponent(text)));
  })();
}
