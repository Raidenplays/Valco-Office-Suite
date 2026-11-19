"use strict";
const { SearchService } = ChromeUtils.importESModule("moz-src:///toolkit/components/search/SearchService.sys.mjs");
/* Internet Options. Every control with data-pref (or a radio group named after a pref)
 * is loaded from and saved to that pref when you press OK or Apply. */
const { ValcoNet } = ChromeUtils.importESModule("resource://valconet/ValcoNet.sys.mjs");
const opener = DialogArgs.opener || Services.wm.getMostRecentWindow("navigator:browser");
const HOME_DEFAULT = "chrome://valconet/content/home.html";

/* ------------------------------------------------------------------ tabs */
const tabs = [...document.querySelectorAll(".tabs button")];
function showSheet(id) {
  for (const t of tabs) t.setAttribute("aria-selected", t.dataset.sheet === id);
  for (const s of document.querySelectorAll(".sheet")) s.hidden = s.id !== id;
  Prefs.set("valconet.options.lastTab", id);
}
tabs.forEach(t => t.addEventListener("click", () => showSheet(t.dataset.sheet)));
showSheet(DialogArgs.tab || Prefs.get("valconet.options.lastTab", "general"));

/* --------------------------------------------------------------- binding */
const typed = (el, raw) => {
  const t = el.dataset.type;
  if (t === "int") return parseInt(raw, 10) || 0;
  if (t === "string") return String(raw);
  return raw;
};
function read(el) {
  const name = el.dataset.pref;
  const v = Prefs.get(name, undefined);
  if (el.type === "checkbox") {
    if ("on" in el.dataset) return String(v) === el.dataset.on;
    return "invert" in el.dataset ? !v : !!v;
  }
  return v === undefined ? "" : String(v);
}
function write(el) {
  const name = el.dataset.pref;
  if (el.type === "checkbox") {
    let v;
    if ("on" in el.dataset) v = typed(el, el.checked ? el.dataset.on : el.dataset.off);
    else v = "invert" in el.dataset ? !el.checked : el.checked;
    Prefs.set(name, v);
    if (el.dataset.also) Prefs.set(el.dataset.also, v);
    return;
  }
  const v = typed(el, el.value.trim());
  Prefs.set(name, v);
  if (el.dataset.alsoText) Prefs.set(el.dataset.alsoText, v);
}
const bound = [...document.querySelectorAll("[data-pref]")];
const radios = {};
for (const r of document.querySelectorAll('input[type="radio"][name]')) {
  (radios[r.name] ||= []).push(r);
}
function load() {
  for (const el of bound) {
    const v = read(el);
    if (el.type === "checkbox") el.checked = v;
    else el.value = v;
  }
  for (const [name, group] of Object.entries(radios)) {
    const v = String(Prefs.get(name, ""));
    for (const r of group) r.checked = r.value === v;
  }
  $("homepages").value = Prefs.get("browser.startup.homepage", HOME_DEFAULT).split("|").join("\n");
}
load();
const dirty = () => ($("apply").disabled = false);
document.addEventListener("change", dirty);
document.addEventListener("input", dirty);

async function apply() {
  for (const el of bound) {
    if (!el.disabled) write(el);
  }
  for (const [name, group] of Object.entries(radios)) {
    const r = group.find(x => x.checked);
    if (r) Prefs.set(name, typed(r, r.value));
  }
  const pages = $("homepages").value.split(/\n+/).map(s => s.trim()).filter(Boolean)
    .map(s => (/^[a-z]+:/i.test(s) ? s : "https://" + s));
  Prefs.set("browser.startup.homepage", pages.length ? pages.join("|") : "about:blank");
  const eng = $("engine").value;
  if (eng) {
    const e = SearchService.getEngineByName(eng);
    if (e) await SearchService.setDefault(e, SearchService.CHANGE_REASON?.USER ?? 0);
  }
  $("apply").disabled = true;
}
$("apply").onclick = apply;
$("ok").onclick = async () => {
  await apply();
  window.close();
};
$("cancel").onclick = () => window.close();

/* -------------------------------------------------------------- general */
$("use-current").onclick = () => {
  const tabsUrls = opener ? opener.gBrowser.visibleTabs.map(t => t.linkedBrowser.currentURI.spec).filter(u => !u.startsWith("about:")) : [];
  const cur = opener?.gBrowser.currentURI.spec;
  $("homepages").value = (cur ? [cur] : tabsUrls).join("\n");
  dirty();
};
$("use-default").onclick = () => { $("homepages").value = HOME_DEFAULT; dirty(); };
$("use-blank").onclick = () => { $("homepages").value = "about:blank"; dirty(); };
$("delete-history").onclick = () => opener?.ValcoNetWindow.openDialog("delete-history");
(async () => {
  await SearchService.init();
  const engines = await SearchService.getVisibleEngines();
  const def = await SearchService.getDefault();
  const sel = $("engine");
  for (const e of engines) {
    const o = document.createElement("option");
    o.value = o.textContent = e.name;
    o.selected = e.name === def.name;
    sel.append(o);
  }
})();
const prefsPane = pane => () => {
  opener?.openPreferences(pane);
  window.close();
};
$("search-settings").onclick = prefsPane("search");
$("fonts").onclick = prefsPane("fonts");
$("languages").onclick = prefsPane("general-language");
$("accessibility").onclick = prefsPane("general");

/* ------------------------------------------------------------- security */
for (const z of document.querySelectorAll(".zone")) {
  z.addEventListener("click", () => {
    for (const o of document.querySelectorAll(".zone")) o.classList.toggle("sel", o === z);
  });
}
$("custom-security").onclick = prefsPane("privacy-trackingprotection");
$("cookie-exceptions").onclick = prefsPane("privacy-cookies");
$("cookie-advanced").onclick = prefsPane("privacy");
$("popup-settings").onclick = prefsPane("privacy-permissions");

/* -------------------------------------------------------------- content */
$("certs").onclick = () => window.openDialog("chrome://pippki/content/certManager.xhtml", "mozilla:certmanager", "chrome,centerscreen,resizable");
$("devices").onclick = () => window.openDialog("chrome://pippki/content/device_manager.xhtml", "mozilla:devicemanager", "chrome,centerscreen,resizable");
$("passwords").onclick = () => {
  opener?.ValcoNetWindow.openURL("about:logins", "tab");
  window.close();
};

function updateSync() {
  let st = { status: "not_configured" };
  try {
    st = ChromeUtils.importESModule("resource://services-sync/UIState.sys.mjs").UIState.get();
  } catch (ex) {}
  const signedOut = st.status === "not_configured";
  $("sync-status").textContent = signedOut
    ? "Sync your favorites, passwords, add-ons, history and open tabs with a Mozilla account."
    : st.status === "signed_in" ? `Signed in as ${st.email}.` : `${st.email || "Your account"} needs you to sign in again.`;
  $("sync-signin").textContent = signedOut ? "Sign in" : st.status === "signed_in" ? "Manage account" : "Sign in again";
}
updateSync();
const syncObserver = () => updateSync();
Services.obs.addObserver(syncObserver, "sync-ui-state:update");
addEventListener("unload", () => Services.obs.removeObserver(syncObserver, "sync-ui-state:update"));
$("sync-signin").onclick = () => {
  const st = ChromeUtils.importESModule("resource://services-sync/UIState.sys.mjs").UIState.get();
  if (st.status === "not_configured") opener?.ValcoNetWindow.syncSignIn();
  else if (st.status === "signed_in") opener?.gSync.openFxAManagePage("valconet");
  else opener?.gSync.openSignInAgainPage("valconet");
  window.close();
};
$("sync-settings").onclick = prefsPane("sync");

/* ---------------------------------------------------------- connections */
$("win-network").onclick = () => opener?.ValcoNetWindow.launchExternal("ms-settings:network-status");
$("win-proxy").onclick = () => opener?.ValcoNetWindow.launchExternal("ms-settings:network-proxy");

/* -------------------------------------------------------------- programs */
function updateDefault() {
  const isDefault = ValcoNet.isDefaultBrowser();
  $("default-status").textContent = isDefault ? "ValcoNet is your default web browser." : "ValcoNet is not currently your default web browser.";
  $("make-default").disabled = isDefault;
}
updateDefault();
$("make-default").onclick = () => {
  ValcoNet.makeDefault();
  addEventListener("focus", updateDefault);
};
$("addons").onclick = () => {
  opener?.BrowserAddonUI?.openAddonsMgr();
  window.close();
};
$("set-programs").onclick = () => opener?.ValcoNetWindow.launchExternal("ms-settings:defaultapps");

/* -------------------------------------------------------------- advanced */
$("about-config").onclick = () => {
  opener?.ValcoNetWindow.openURL("about:config", "tab");
  window.close();
};
$("gecko-settings").onclick = prefsPane("general");
$("reset").onclick = () => {
  if (!Services.prompt.confirm(window, "Reset ValcoNet Settings", "Reset ValcoNet's toolbars and options to their defaults?")) return;
  for (const name of Services.prefs.getChildList("valconet.")) {
    if (name !== "valconet.firstRun.done" && name !== "valconet.feeds") Services.prefs.clearUserPref(name);
  }
  load();
  Services.prompt.alert(window, "Reset ValcoNet Settings", "ValcoNet's settings were reset. The toolbar layout is restored the next time ValcoNet starts.");
};
