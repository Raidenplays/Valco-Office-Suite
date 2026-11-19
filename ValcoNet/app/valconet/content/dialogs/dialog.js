"use strict";
/* Shared dialog chrome. Each dialog sets <title> and windowtype; this adds the glass titlebar. */
const DialogArgs = (window.arguments && window.arguments[0]) || {};
const $ = id => document.getElementById(id);
const Prefs = {
  get(name, dflt) {
    try {
      switch (Services.prefs.getPrefType(name)) {
        case Services.prefs.PREF_BOOL: return Services.prefs.getBoolPref(name);
        case Services.prefs.PREF_INT: return Services.prefs.getIntPref(name);
        case Services.prefs.PREF_STRING: return Services.prefs.getStringPref(name);
      }
    } catch (ex) {}
    return dflt;
  },
  set(name, value) {
    if (typeof value === "boolean") Services.prefs.setBoolPref(name, value);
    else if (typeof value === "number") Services.prefs.setIntPref(name, value);
    else Services.prefs.setStringPref(name, value);
  },
};

(function frame() {
  document.documentElement.setAttribute("customtitlebar", "true");
  const client = document.createElement("div");
  client.id = "vn-client";
  client.append(...document.body.childNodes);
  const bar = document.createElement("div");
  bar.id = "vn-titlebar";
  const icon = document.createElement("img");
  icon.src = document.querySelector("link[rel=icon]")?.href || "chrome://valconet/skin/icons/valconet.svg";
  const title = document.createElement("div");
  title.id = "vn-title";
  title.textContent = document.title;
  const close = document.createElement("button");
  close.id = "vn-close";
  close.title = "Close";
  close.addEventListener("click", () => window.close());
  bar.append(icon, title, close);
  document.body.append(bar, client);
  addEventListener("keydown", e => {
    if (e.key === "Escape") window.close();
  });
  addEventListener("load", () => {
    requestAnimationFrame(() => {
      try { window.sizeToContent(); } catch (ex) {}
    });
  });
})();
