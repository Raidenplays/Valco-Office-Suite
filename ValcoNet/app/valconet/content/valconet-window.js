/* ValcoNet browser-window layer. Loaded into every browser.xhtml window.
 * build() runs when the window's DOM is interactive; init() after Gecko's
 * delayed startup, when gBrowser and friends are ready. */

"use strict";

window.ValcoNetWindow = (() => {
  const { ValcoWidgets } = ChromeUtils.importESModule("resource://valconet/ValcoWidgets.sys.mjs");
  const { ValcoNet, VALCONET_VERSION } = ChromeUtils.importESModule("resource://valconet/ValcoNet.sys.mjs");
  const { ValcoFeeds } = ChromeUtils.importESModule("resource://valconet/ValcoFeeds.sys.mjs");
  const { SearchService } = ChromeUtils.importESModule("moz-src:///toolkit/components/search/SearchService.sys.mjs");
  const { PrivateBrowsingUtils: PBU } = ChromeUtils.importESModule(
    "resource://gre/modules/PrivateBrowsingUtils.sys.mjs"
  );

  const HTML_NS = "http://www.w3.org/1999/xhtml";
  const $ = id => document.getElementById(id);
  const isPrivate = () => PBU.isWindowPrivate(window);
  const pref = (name, dflt) => {
    try {
      switch (Services.prefs.getPrefType(name)) {
        case Services.prefs.PREF_BOOL:
          return Services.prefs.getBoolPref(name);
        case Services.prefs.PREF_INT:
          return Services.prefs.getIntPref(name);
        case Services.prefs.PREF_STRING:
          return Services.prefs.getStringPref(name);
      }
    } catch (ex) {}
    return dflt;
  };

  function xul(tag, attrs = {}, children = []) {
    const n = document.createXULElement(tag);
    for (const [k, v] of Object.entries(attrs)) {
      if (k.startsWith("on")) {
        if (typeof v === "function") {
          n.addEventListener(k.slice(2), v);
        }
      } else if (v !== null && v !== undefined && v !== false) {
        n.setAttribute(k, v === true ? "true" : v);
      }
    }
    n.append(...children);
    return n;
  }
  function html(tag, attrs = {}, children = []) {
    const n = document.createElementNS(HTML_NS, tag);
    for (const [k, v] of Object.entries(attrs)) {
      if (k.startsWith("on") && typeof v === "function") {
        n.addEventListener(k.slice(2), v);
      } else if (k === "text") {
        n.textContent = v;
      } else if (v !== null && v !== undefined && v !== false) {
        n.setAttribute(k, v);
      }
    }
    n.append(...children);
    return n;
  }
  /** Menu helpers for the command-bar popups. */
  function item(label, oncommand, extra = {}) {
    // Always iconic so every item lines up with the Vista icon gutter.
    const cls = extra.type ? extra.class : ["menuitem-iconic", extra.class].filter(Boolean).join(" ");
    let acceltext = null;
    if (label.includes("\t")) {
      [label, acceltext] = label.split("\t");
    }
    return xul("menuitem", { label, acceltext, oncommand, ...extra, class: cls });
  }
  const sep = () => xul("menuseparator");
  function submenu(label, children, extra = {}) {
    const popup = xul("menupopup", { class: "valco-menu" }, children);
    return xul("menu", { label, class: "menu-iconic", ...extra }, [popup]);
  }

  /** Call a Gecko command by trying the current and older function names. */
  function call(...fns) {
    for (const fn of fns) {
      try {
        return fn();
      } catch (ex) {
        if (!(ex instanceof TypeError || ex instanceof ReferenceError)) {
          throw ex;
        }
      }
    }
    return undefined;
  }
  function doCmd(id) {
    const c = $(id);
    if (c && !c.hasAttribute("disabled")) {
      c.doCommand();
      return true;
    }
    return false;
  }

  const ZOOM_STEPS = [0.5, 0.75, 1, 1.25, 1.5, 2, 4];
  const GROUP_COLORS = 6;

  const VN = {
    version: VALCONET_VERSION,

    // ------------------------------------------------------------------ build
    build() {
      ValcoWidgets.ensure();
      document.documentElement.toggleAttribute("valconet-private", isPrivate());
      this.buildTitlebar();
      this.buildStatusbar();
      this.buildOverlays();
      this.applyVisibilityPrefs();
    },

    buildTitlebar() {
      const toolbox = $("navigator-toolbox");
      if (!toolbox || $("valco-titlebar")) {
        return;
      }
      const bar = xul("hbox", { id: "valco-titlebar", align: "center" }, [
        xul("image", { id: "valco-title-icon" }),
        xul("label", { id: "valco-title-text", crop: "end", flex: "1", value: "ValcoNet" }),
        xul("hbox", { id: "valco-inprivate-title", align: "center", hidden: !isPrivate() }, [
          xul("image", { class: "valco-inprivate-icon" }),
          xul("label", { value: "InPrivate" }),
        ]),
        xul("hbox", { id: "valco-caption", align: "start" }, [
          xul("toolbarbutton", { id: "valco-cap-min", class: "valco-cap", command: "cmd_minimizeWindow", tooltiptext: "Minimize" }),
          xul("toolbarbutton", { id: "valco-cap-max", class: "valco-cap", command: "cmd_maximizeWindow", tooltiptext: "Maximize" }),
          xul("toolbarbutton", { id: "valco-cap-restore", class: "valco-cap", command: "cmd_restoreWindow", tooltiptext: "Restore Down" }),
          xul("toolbarbutton", { id: "valco-cap-close", class: "valco-cap", command: "cmd_closeWindow", tooltiptext: "Close" }),
        ]),
      ]);
      bar.addEventListener("dblclick", e => {
        if (e.button === 0 && !e.target.closest(".valco-cap")) {
          window.windowState === window.STATE_MAXIMIZED ? window.restore() : window.maximize();
        }
      });
      toolbox.prepend(bar);
    },

    buildStatusbar() {
      const browser = $("browser");
      if (!browser || $("valco-statusbar")) {
        return;
      }
      const zoomPopup = xul("menupopup", {
        id: "valco-zoom-popup",
        class: "valco-menu",
        onpopupshowing: e => this.fillZoomMenu(e.target),
      });
      const bar = xul("hbox", { id: "valco-statusbar", align: "center" }, [
        xul("hbox", { id: "valco-status-main", flex: "1", align: "center" }, [
          xul("image", { id: "valco-status-icon" }),
          xul("label", { id: "valco-status-text", crop: "center", flex: "1", value: "Done" }),
        ]),
        xul("hbox", { id: "valco-status-progress", class: "valco-status-pane", align: "center", hidden: "true" }, [
          html("div", { id: "valco-progress" }, [html("div", { id: "valco-progress-fill" })]),
        ]),
        xul("hbox", { id: "valco-status-popups", class: "valco-status-pane", align: "center", hidden: "true", tooltiptext: "Pop-ups were blocked on this page", onclick: () => this.showPopupNotification() }, [
          xul("image", { class: "valco-status-img" }),
        ]),
        xul("hbox", { id: "valco-status-security", class: "valco-status-pane", align: "center", hidden: "true", tooltiptext: "Secure connection", onclick: () => this.openSiteInfo() }, [
          xul("image", { class: "valco-status-img" }),
        ]),
        xul("hbox", { id: "valco-status-zone", class: "valco-status-pane", align: "center" }, [
          xul("image", { id: "valco-zone-icon" }),
          xul("label", { id: "valco-zone-text", value: "Internet | Protected Mode: On" }),
        ]),
        xul("toolbarbutton", { id: "valco-status-zoom", class: "valco-status-pane", label: "100%", tooltiptext: "Change zoom level", onclick: e => this.onZoomClick(e) }),
        xul("hbox", { id: "valco-status-zoom-arrow", class: "valco-status-pane" }, [
          xul("toolbarbutton", { id: "valco-zoom-menu", type: "menu", wantdropmarker: "true", tooltiptext: "Zoom levels" }, [zoomPopup]),
        ]),
        xul("box", { id: "valco-gripper" }),
      ]);
      browser.after(bar);
    },

    buildOverlays() {
      const browser = $("browser");
      if (!browser || $("valco-favcenter")) {
        return;
      }
      // Favorites Center: a flyout over the page, or docked when pinned.
      const fav = xul("vbox", { id: "valco-favcenter", hidden: "true" }, [
        html("iframe", { id: "valco-favcenter-frame", flex: "1" }),
      ]);
      const splitter = xul("splitter", { id: "valco-favcenter-splitter", hidden: "true", resizebefore: "sibling", resizeafter: "none" });
      browser.prepend(fav, splitter);
      // Quick Tabs.
      const qt = html("div", { id: "valco-quicktabs", hidden: "true" });
      browser.append(qt);
    },

    // ------------------------------------------------------------------- init
    init() {
      ValcoWidgets.applyLayout();
      try {
        const vis = Services.prefs.getStringPref("browser.toolbars.bookmarks.visibility", "always");
        setToolbarVisibility($("PersonalToolbar"), vis === "always" ? "always" : vis, false);
      } catch (ex) {}
      this.initPrivate();
      this.initTitle();
      this.initUrlbar();
      this.initStatus();
      this.initProgress();
      this.initTabGroups();
      this.initMenubar();
      this.initKeys();
      this.initFeeds();
      this.initSearch();
      this.prefObserver = {
        observe: () => this.applyVisibilityPrefs(),
      };
      Services.prefs.addObserver("valconet.", this.prefObserver);
      window.addEventListener("unload", () => {
        Services.prefs.removeObserver("valconet.", this.prefObserver);
        try {
          gBrowser.removeTabsProgressListener(this.tabsListener);
          gBrowser.removeProgressListener(this.progressListener);
        } catch (ex) {}
      });
      this.updateAll();
    },

    initPrivate() {
      if (!isPrivate()) {
        return;
      }
      $("valco-inprivate-title").hidden = false;
      const box = document.querySelector("#urlbar .urlbar-input-container");
      if (box && !$("valco-inprivate-badge")) {
        box.prepend(
          xul("hbox", { id: "valco-inprivate-badge", align: "center", tooltiptext: "InPrivate Browsing is turned on" }, [
            xul("image", { class: "valco-inprivate-icon" }),
            xul("label", { value: "InPrivate" }),
          ])
        );
      }
    },

    applyVisibilityPrefs() {
      $("valco-statusbar")?.toggleAttribute("hidden", !pref("valconet.statusbar.visible", true));
      document.documentElement.toggleAttribute("valco-cmdlabels", pref("valconet.commandbar.labels", true));
      document.documentElement.toggleAttribute("valco-tabcolors", pref("valconet.tabgroups.colors", true));
      const glass = pref("valconet.glass", "auto");
      document.documentElement.setAttribute("valco-glass", glass);
      const menubar = $("toolbar-menubar");
      if (menubar) {
        const always = pref("valconet.menubar.alwaysShow", false);
        if (always !== (menubar.getAttribute("autohide") !== "true")) {
          try {
            setToolbarVisibility(menubar, always, false);
          } catch (ex) {
            menubar.setAttribute("autohide", always ? "false" : "true");
          }
        }
      }
    },

    // ----------------------------------------------------------------- title
    initTitle() {
      const update = () => this.updateTitle();
      gBrowser.tabContainer.addEventListener("TabSelect", update);
      gBrowser.tabContainer.addEventListener("TabAttrModified", e => {
        if (e.target === gBrowser.selectedTab) {
          update();
          this.updateFavicon();
        }
      });
      window.addEventListener("sizemodechange", () => this.updateSizemode());
      this.updateSizemode();
    },

    updateSizemode() {
      document.documentElement.setAttribute(
        "valco-sizemode",
        window.windowState === window.STATE_MAXIMIZED ? "maximized" : window.windowState === window.STATE_FULLSCREEN ? "fullscreen" : "normal"
      );
    },

    updateTitle() {
      const tab = gBrowser.selectedTab;
      let title = tab.label || "";
      const uri = gBrowser.currentURI?.spec || "";
      if (uri === "about:privatebrowsing" || (isPrivate() && uri.startsWith("chrome://valconet/content/newtab"))) {
        title = "InPrivate";
      }
      const suffix = isPrivate() ? "ValcoNet - [InPrivate]" : "ValcoNet";
      $("valco-title-text").value = title ? `${title} - ${suffix}` : suffix;
    },

    // ---------------------------------------------------------------- urlbar
    initUrlbar() {
      const container = document.querySelector("#urlbar .urlbar-input-container");
      const trust = $("trust-icon-container") || $("identity-box");
      if (trust && !$("valco-urlbar-favicon")) {
        trust.append(xul("image", { id: "valco-urlbar-favicon" }));
      }
      const pab = $("page-action-buttons");
      if (pab && !$("valco-urlbar-lock")) {
        pab.append(
          xul("image", {
            id: "valco-urlbar-lock",
            class: "urlbar-icon",
            hidden: "true",
            tooltiptext: "Security Report",
            onclick: () => this.openSiteInfo(),
          })
        );
        pab.append(
          xul("image", {
            id: "valco-urlbar-dropmarker",
            class: "urlbar-icon",
            tooltiptext: "Show Address Bar Autocomplete",
            onmousedown: e => {
              e.preventDefault();
              gURLBar.focus();
              try {
                gURLBar.startQuery({ allowAutofill: false, event: e });
              } catch (ex) {
                gURLBar.inputField?.dispatchEvent(new KeyboardEvent("keydown", { key: "ArrowDown", keyCode: 40, bubbles: true }));
              }
            },
          })
        );
      }
      container?.setAttribute("valco-ready", "true");
    },

    updateFavicon() {
      const img = $("valco-urlbar-favicon");
      if (!img) {
        return;
      }
      const tab = gBrowser.selectedTab;
      const src = tab.image || tab.getAttribute("image");
      if (src && !tab.hasAttribute("busy")) {
        img.setAttribute("src", src);
        img.removeAttribute("default");
      } else {
        img.removeAttribute("src");
        img.setAttribute("default", "true");
      }
    },

    openSiteInfo() {
      const anchor = $("trust-icon-container") || $("identity-icon-box");
      anchor?.click();
    },

    // ---------------------------------------------------------------- status
    initStatus() {
      const label = $("statuspanel-label");
      const panel = $("statuspanel");
      if (label && panel) {
        this.statusObserver = new MutationObserver(() => this.updateStatusText());
        this.statusObserver.observe(label, { attributes: true, attributeFilter: ["value"] });
        this.statusObserver.observe(panel, { attributes: true, attributeFilter: ["inactive", "hidden", "type"] });
      }
      window.addEventListener("FullZoomChange", () => this.updateZoom());
      window.addEventListener("TextZoomChange", () => this.updateZoom());
      gBrowser.tabContainer.addEventListener("TabSelect", () => {
        this.updateAll();
        if (!this.feedsByBrowser.has(gBrowser.selectedBrowser)) {
          this.scanFeeds(gBrowser.selectedBrowser);
        }
      });
      // Pop-up blocked notifications.
      gBrowser.addEventListener("DOMUpdateBlockedPopups", () => this.updatePopups());
    },

    updateStatusText() {
      const label = $("statuspanel-label");
      const panel = $("statuspanel");
      const out = $("valco-status-text");
      if (!out) {
        return;
      }
      const busy = gBrowser.selectedTab.hasAttribute("busy");
      let text = "";
      if (panel && !panel.hasAttribute("inactive") && !panel.hidden) {
        text = label.getAttribute("value") || label.value || "";
      }
      if (!text) {
        const uri = gBrowser.currentURI?.spec || "";
        text = busy ? `Waiting for ${uri}...` : this.errorPage ? "Error on page." : "Done";
      }
      out.value = text;
      $("valco-status-icon").setAttribute("state", busy ? "busy" : this.errorPage ? "error" : "done");
    },

    updateZoom() {
      const btn = $("valco-status-zoom");
      if (!btn) {
        return;
      }
      let z = 1;
      try {
        z = ZoomManager.getZoomForBrowser(gBrowser.selectedBrowser);
      } catch (ex) {}
      btn.label = Math.round(z * 100) + "%";
    },

    onZoomClick(e) {
      if (e.button !== 0) {
        return;
      }
      // IE cycles 100% → 125% → 150% → 100%.
      const z = ZoomManager.getZoomForBrowser(gBrowser.selectedBrowser);
      const next = z < 1.24 ? 1.25 : z < 1.49 ? 1.5 : 1;
      this.setZoom(next);
    },

    setZoom(z) {
      try {
        ZoomManager.setZoomForBrowser(gBrowser.selectedBrowser, z);
        FullZoom._applyZoomToPref?.(gBrowser.selectedBrowser);
      } catch (ex) {
        console.error(ex);
      }
      this.updateZoom();
    },

    fillZoomMenu(popup) {
      popup.replaceChildren(
        item("Zoom In\tCtrl++", () => FullZoom.enlarge()),
        item("Zoom Out\tCtrl+-", () => FullZoom.reduce()),
        sep(),
        ...[4, 2, 1.5, 1.25, 1, 0.75, 0.5].map(z =>
          item(`${Math.round(z * 100)}%`, () => this.setZoom(z), {
            type: "radio",
            checked: Math.abs(ZoomManager.getZoomForBrowser(gBrowser.selectedBrowser) - z) < 0.01,
          })
        ),
        sep(),
        item("Custom...", () => this.customZoom())
      );
    },

    customZoom() {
      const val = { value: String(Math.round(ZoomManager.getZoomForBrowser(gBrowser.selectedBrowser) * 100)) };
      if (Services.prompt.prompt(window, "Custom Zoom", "Percentage zoom (10–500):", val, null, {})) {
        const n = parseInt(val.value, 10);
        if (n >= 10 && n <= 500) {
          this.setZoom(n / 100);
        }
      }
    },

    updatePopups() {
      const pane = $("valco-status-popups");
      if (!pane) {
        return;
      }
      const b = gBrowser.selectedBrowser;
      let count = 0;
      try {
        count = b.popupAndRedirectBlocker?.getBlockedPopupCount?.() ?? b.popupBlocker?.getBlockedPopupCount?.() ?? 0;
      } catch (ex) {}
      pane.hidden = !count;
    },

    showPopupNotification() {
      try {
        gPopupBlockerObserver?.toggleAllowPopupsForSite?.();
      } catch (ex) {}
    },

    updateZone() {
      const uri = gBrowser.currentURI;
      let zone = "internet";
      let label = "Internet";
      let pm = true;
      try {
        const scheme = uri.scheme;
        if (scheme === "file" || scheme === "chrome" || scheme === "resource" || scheme === "about" || scheme === "moz-extension") {
          zone = "computer";
          label = "Computer";
          pm = scheme === "file" || scheme === "about";
        } else if (scheme === "http" || scheme === "https") {
          const host = uri.host;
          if (
            host === "localhost" ||
            /\.(local|lan|home|internal|localhost)$/.test(host) ||
            /^(127\.|10\.|192\.168\.|172\.(1[6-9]|2\d|3[01])\.|\[?::1\]?$)/.test(host) ||
            !host.includes(".")
          ) {
            zone = "intranet";
            label = "Local intranet";
          }
        }
      } catch (ex) {}
      if (isPrivate()) {
        label += " | InPrivate";
      }
      $("valco-zone-text").value = `${label} | Protected Mode: ${pm ? "On" : "Off"}`;
      $("valco-zone-icon").setAttribute("zone", zone);
    },

    updateSecurity() {
      let secure = false;
      let broken = false;
      try {
        const state = gBrowser.securityUI.state;
        secure = !!(state & Ci.nsIWebProgressListener.STATE_IS_SECURE);
        broken = !!(state & Ci.nsIWebProgressListener.STATE_IS_BROKEN);
      } catch (ex) {}
      const uri = gBrowser.currentURI?.spec || "";
      const docURI = gBrowser.selectedBrowser.documentURI?.spec || "";
      const certError = docURI.startsWith("about:certerror");
      this.errorPage = docURI.startsWith("about:neterror") || certError;
      const urlbar = $("urlbar");
      urlbar?.setAttribute("valco-security", certError ? "error" : secure ? "secure" : broken && uri.startsWith("https") ? "broken" : "none");
      $("valco-urlbar-lock")?.toggleAttribute("hidden", !secure);
      $("valco-status-security") && ($("valco-status-security").hidden = !secure);
    },

    updateNav() {
      const busy = gBrowser.selectedTab.hasAttribute("busy");
      const stop = $("valco-stop");
      if (stop) {
        stop.toggleAttribute("disabled", !busy);
      }
      $("valco-history-button")?.toggleAttribute(
        "disabled",
        !gBrowser.canGoBack && !gBrowser.canGoForward
      );
      this.updateFeedButton();
    },

    updateAll() {
      try {
        this.updateTitle();
        this.updateFavicon();
        this.updateZone();
        this.updateSecurity();
        this.updateZoom();
        this.updatePopups();
        this.updateNav();
        this.updateStatusText();
        this.setProgress(gBrowser.selectedTab.hasAttribute("busy") ? 0.05 : null);
      } catch (ex) {
        console.error("ValcoNet: update", ex);
      }
    },

    // -------------------------------------------------------------- progress
    initProgress() {
      const self = this;
      const NSL = Ci.nsIWebProgressListener;
      this.progressListener = {
        QueryInterface: ChromeUtils.generateQI(["nsIWebProgressListener", "nsISupportsWeakReference"]),
        onStateChange(webProgress, request, flags) {
          if (!webProgress.isTopLevel) {
            return;
          }
          if (flags & NSL.STATE_START && flags & NSL.STATE_IS_NETWORK) {
            self.setProgress(0.05);
          } else if (flags & NSL.STATE_STOP && flags & NSL.STATE_IS_NETWORK) {
            self.setProgress(null);
            self.scanFeeds(gBrowser.selectedBrowser);
          }
          self.updateNav();
          self.updateStatusText();
        },
        onProgressChange(webProgress, request, curSelf, maxSelf, curTotal, maxTotal) {
          if (maxTotal > 0) {
            self.setProgress(Math.max(0.05, curTotal / maxTotal));
          }
        },
        onLocationChange(webProgress, request, location, flags) {
          if (!webProgress.isTopLevel) {
            return;
          }
          if (location?.spec === "about:privatebrowsing" && isPrivate()) {
            // Show ValcoNet's "InPrivate is turned on" page instead of Gecko's.
            gBrowser.selectedBrowser.loadURI(Services.io.newURI("chrome://valconet/content/newtab.html"), {
              triggeringPrincipal: Services.scriptSecurityManager.getSystemPrincipal(),
              flags: Ci.nsIWebNavigation.LOAD_FLAGS_REPLACE_HISTORY,
            });
            return;
          }
          self.updateTitle();
          self.updateZone();
          self.updateSecurity();
          self.updateNav();
          self.updateZoom();
          self.updatePopups();
          self.closeFavoritesCenter(true);
        },
        onStatusChange() {
          self.updateStatusText();
        },
        onSecurityChange() {
          self.updateSecurity();
        },
        onContentBlockingEvent() {},
      };
      gBrowser.addProgressListener(this.progressListener);
      // Grow the bar a little while a page loads without byte counts.
      this.progressTimer = setInterval(() => {
        if (this.progress != null && this.progress < 0.9) {
          this.setProgress(this.progress + (0.9 - this.progress) * 0.06);
        }
      }, 250);
    },

    setProgress(value) {
      this.progress = value;
      const pane = $("valco-status-progress");
      if (!pane) {
        return;
      }
      if (value == null) {
        pane.hidden = true;
        return;
      }
      pane.hidden = false;
      $("valco-progress-fill").style.width = Math.round(Math.min(1, value) * 100) + "%";
    },

    // ----------------------------------------------------------- tab groups
    initTabGroups() {
      this.nextGroup = 0;
      gBrowser.tabContainer.addEventListener("TabOpen", e => {
        const tab = e.target;
        const opener = tab.openerTab;
        if (!opener || opener === tab) {
          return;
        }
        let g = opener.getAttribute("valco-group");
        if (g === null) {
          g = String(this.nextGroup++ % GROUP_COLORS);
          opener.setAttribute("valco-group", g);
        }
        tab.setAttribute("valco-group", g);
      });
      gBrowser.tabContainer.addEventListener("TabClose", () => {
        // Ungroup tabs left on their own.
        setTimeout(() => {
          const counts = {};
          for (const t of gBrowser.tabs) {
            const g = t.getAttribute("valco-group");
            if (g !== null) {
              counts[g] = (counts[g] || 0) + 1;
            }
          }
          for (const t of gBrowser.tabs) {
            const g = t.getAttribute("valco-group");
            if (g !== null && counts[g] < 2) {
              t.removeAttribute("valco-group");
            }
          }
        }, 0);
      });
    },

    ungroupTabs() {
      for (const t of gBrowser.tabs) {
        t.removeAttribute("valco-group");
      }
    },

    // ------------------------------------------------------------- menu bar
    initMenubar() {
      const relabel = (id, label, accesskey) => {
        const n = $(id);
        if (n) {
          n.removeAttribute("data-l10n-id");
          n.setAttribute("label", label);
          if (accesskey) {
            n.setAttribute("accesskey", accesskey);
          }
        }
      };
      relabel("bookmarksMenu", "Favorites", "a");
      $("history-menu")?.setAttribute("hidden", "true");
      const bmPopup = $("bookmarksMenuPopup");
      bmPopup?.addEventListener("popupshown", () => {
        relabel("bookmarksShowAll", "Organize Favorites...");
        const bookmarkThis = $("menu_bookmarkThisPage");
        if (bookmarkThis) {
          bookmarkThis.removeAttribute("data-l10n-id");
          bookmarkThis.setAttribute("label", "Add to Favorites...");
          bookmarkThis.removeAttribute("command");
          if (!bookmarkThis._valco) {
            bookmarkThis._valco = true;
            bookmarkThis.addEventListener("command", e => {
              e.stopImmediatePropagation();
              this.addFavorite();
            });
          }
        }
      });
      // Tools menu: Internet Options at the bottom, like IE.
      const toolsPopup = $("menu_ToolsPopup");
      if (toolsPopup && !$("valco-menu-options")) {
        toolsPopup.append(
          xul("menuseparator"),
          xul("menuitem", { id: "valco-menu-delete-history", label: "Delete Browsing History...", accesskey: "D", oncommand: () => this.openDialog("delete-history") }),
          xul("menuitem", { id: "valco-menu-options", label: "Internet Options", accesskey: "O", oncommand: () => this.openDialog("options") })
        );
      }
      // View menu: Explorer Bars + Quick Tabs.
      const viewPopup = $("menu_viewPopup");
      if (viewPopup && !$("valco-menu-explorerbars")) {
        viewPopup.append(
          xul("menuseparator"),
          submenu(
            "Explorer Bars",
            [
              item("Favorites\tCtrl+Shift+I", () => this.openFavoritesCenter("favorites", true)),
              item("History\tCtrl+Shift+H", () => this.openFavoritesCenter("history", true)),
              item("Feeds\tCtrl+Shift+J", () => this.openFavoritesCenter("feeds", true)),
            ],
            { id: "valco-menu-explorerbars", accesskey: "x" }
          ),
          xul("menuitem", { label: "Quick Tabs", acceltext: "Ctrl+Q", oncommand: () => this.toggleQuickTabs() }),
          xul("menuitem", { label: "Status Bar", type: "checkbox", id: "valco-menu-statusbar", oncommand: () => this.togglePref("valconet.statusbar.visible") })
        );
        viewPopup.addEventListener("popupshowing", () => {
          $("valco-menu-statusbar")?.setAttribute("checked", pref("valconet.statusbar.visible", true));
        });
      }
      // Help → About ValcoNet.
      const about = $("aboutName");
      if (about) {
        about.removeAttribute("data-l10n-id");
        about.setAttribute("label", "About ValcoNet");
        about.removeAttribute("oncommand");
        about.addEventListener("command", e => {
          e.stopImmediatePropagation();
          this.openDialog("about");
        });
      }
    },

    togglePref(name) {
      Services.prefs.setBoolPref(name, !pref(name, true));
    },

    // ----------------------------------------------------------------- keys
    initKeys() {
      const mainKeyset = $("mainKeyset");
      if (!mainKeyset || $("valco-keyset")) {
        return;
      }
      // Re-point some of Gecko's own shortcuts at the IE equivalents.
      // (Gecko's chrome CSP forbids string handlers, so these are listeners that stop Gecko's own.)
      const repoint = (id, fn) => {
        const k = $(id);
        if (k) {
          k.removeAttribute("command");
          k.removeAttribute("oncommand");
          k.addEventListener("command", e => {
            e.stopImmediatePropagation();
            fn();
          });
        }
      };
      repoint("key_gotoHistory", () => this.openFavoritesCenter("history"));
      repoint("viewBookmarksSidebarWinKb", () => this.openFavoritesCenter("favorites"));
      repoint("viewBookmarksSidebarKb", () => this.openFavoritesCenter("favorites"));
      repoint("key_sanitize", () => this.openDialog("delete-history"));
      repoint("bookmarkAllTabsKb", () => this.addFavorite());
      repoint("addBookmarkAsKb", () => this.addFavorite());
      repoint("key_quitApplication", () => this.toggleQuickTabs());

      const keys = [
        ["valco-key-quicktabs", "Q", "accel", () => this.toggleQuickTabs()],
        ["valco-key-favcenter", "C", "alt", () => this.toggleFavoritesCenter()],
        ["valco-key-pinfav", "I", "accel,shift", () => this.openFavoritesCenter("favorites", true)],
        ["valco-key-pinhist", "H", "accel,shift", () => this.openFavoritesCenter("history", true)],
        ["valco-key-pinfeeds", "J", "accel,shift", () => this.openFavoritesCenter("feeds", true)],
        ["valco-key-feeds", "J", "alt", () => this.openMenu("valco-cmd-feeds-arrow")],
        ["valco-key-page", "P", "alt", () => this.openMenu("valco-cmd-page")],
        ["valco-key-safety", "S", "alt", () => this.openMenu("valco-cmd-safety")],
        ["valco-key-tools", "O", "alt", () => this.openMenu("valco-cmd-tools")],
        ["valco-key-help", "L", "alt", () => this.openMenu("valco-cmd-help")],
        ["valco-key-print", "R", "alt", () => this.openMenu("valco-cmd-print-arrow")],
        ["valco-key-add", "Z", "alt", () => this.addFavorite()],
      ];
      const keyset = xul(
        "keyset",
        { id: "valco-keyset" },
        keys.map(([id, key, modifiers, fn]) => xul("key", { id, key, modifiers, reserved: "false", oncommand: fn }))
      );
      mainKeyset.after(keyset);
      // Reattach so the edited <key>s take effect.
      const parent = mainKeyset.parentNode;
      const next = mainKeyset.nextSibling;
      mainKeyset.remove();
      parent.insertBefore(mainKeyset, next);
    },

    openMenu(id) {
      const b = $(id);
      if (b) {
        b.open = true;
      }
    },

    // -------------------------------------------------------------- search
    initSearch() {
      const sb = $("searchbar");
      if (!sb) {
        return;
      }
      const setPlaceholder = async () => {
        try {
          const engine = isPrivate() ? await SearchService.getDefaultPrivate() : await SearchService.getDefault();
          const input = sb.inputField || sb.querySelector("input");
          input?.setAttribute("placeholder", engine.name);
        } catch (ex) {}
      };
      setPlaceholder();
      Services.obs.addObserver(setPlaceholder, "browser-search-engine-modified");
      window.addEventListener("unload", () => Services.obs.removeObserver(setPlaceholder, "browser-search-engine-modified"));
    },

    // --------------------------------------------------------------- feeds
    initFeeds() {
      this.feedsByBrowser = new WeakMap();
    },
    /** Called by the ValcoFeeds actor when a page advertises feeds. */
    onFeeds(browser, feeds) {
      this.feedsByBrowser.set(browser, feeds);
      if (browser === gBrowser.selectedBrowser) {
        this.updateFeedButton();
      }
    },
    async scanFeeds(browser) {
      try {
        const actor = browser.browsingContext?.currentWindowGlobal?.getActor("ValcoFeeds");
        const data = await actor.sendQuery("ValcoFeeds:Scan");
        actor.receiveMessage({ name: "ValcoFeeds:Found", data });
      } catch (ex) {
        // Pages the actor doesn't cover (about:, chrome:) simply have no feeds.
        this.onFeeds(browser, []);
      }
    },
    currentFeeds() {
      const b = gBrowser.selectedBrowser;
      const feeds = this.feedsByBrowser.get(b) || [];
      // Drop feeds from a previous page in this browser.
      const docURI = b.currentURI?.spec;
      return feeds.filter(f => f.page === docURI);
    },
    updateFeedButton() {
      const feeds = this.currentFeeds();
      const btn = $("valco-cmd-feeds");
      if (!btn) {
        return;
      }
      btn.toggleAttribute("found", feeds.length > 0);
      btn.setAttribute("tooltiptext", feeds.length ? `View Feeds on this Page (Alt+J)` : "No Feeds Detected on this page (Alt+J)");
    },
    openFeed(url) {
      this.openURL("chrome://valconet/content/feed.html?url=" + encodeURIComponent(url), "current");
    },

    // ------------------------------------------------------- command menus
    runCommand(name, event) {
      switch (name) {
        case "home":
          call(() => BrowserCommands.home(event), () => BrowserHome(event));
          break;
        case "feeds": {
          const feeds = this.currentFeeds();
          if (feeds.length) {
            this.openFeed(feeds[0].href);
          } else {
            this.openFavoritesCenter("feeds");
          }
          break;
        }
        case "mail":
          this.readMail();
          break;
        case "print":
          doCmd("cmd_print");
          break;
      }
    },

    fillMenu(key, popup) {
      const items = (this.menus[key] || (() => []))();
      popup.replaceChildren(...items);
    },

    get menus() {
      const self = this;
      const b = () => gBrowser.selectedBrowser;
      return {
        home() {
          const pages = self.homePages();
          return [
            ...pages.map(url =>
              item(self.prettyURL(url), () => self.openURL(url, "current"))
            ),
            sep(),
            item("Add or Change Home Page...", () => self.openDialog("homepage")),
            submenu("Remove", [
              ...pages.map(url => item(self.prettyURL(url), () => self.removeHomePage(url))),
              sep(),
              item("Remove All...", () => self.removeHomePage(null)),
            ]),
          ];
        },
        feeds() {
          const feeds = self.currentFeeds();
          const list = feeds.length
            ? feeds.map(f => item(f.title || f.href, () => self.openFeed(f.href), { image: "chrome://valconet/skin/icons/feeds.svg" }))
            : [item("No Feeds Detected on this page", null, { disabled: "true" })];
          return [...list, sep(), item("View Feeds in Favorites Center\tCtrl+Shift+J", () => self.openFavoritesCenter("feeds", true))];
        },
        mail() {
          return [
            item("Read Mail", () => self.readMail()),
            item("New Message...", () => self.mailto("")),
            item("Send a Link...", () => self.mailto(`?subject=${encodeURIComponent(gBrowser.selectedTab.label)}&body=${encodeURIComponent(gBrowser.currentURI.spec)}`)),
            item("Send Page...", () => self.mailto(`?subject=${encodeURIComponent(gBrowser.selectedTab.label)}&body=${encodeURIComponent(gBrowser.selectedTab.label + "\n" + gBrowser.currentURI.spec)}`)),
          ];
        },
        print() {
          return [
            item("Print...\tCtrl+P", () => doCmd("cmd_print")),
            item("Print Preview...", () => doCmd("cmd_printPreviewToggle") || doCmd("cmd_print")),
            item("Page Setup...", () => doCmd("cmd_pageSetup") || doCmd("cmd_print")),
          ];
        },
        page() {
          return [
            item("New Tab\tCtrl+T", () => call(() => BrowserCommands.openTab(), () => BrowserOpenTab())),
            item("New Window\tCtrl+N", () => OpenBrowserWindow()),
            item("Duplicate Tab", () => call(() => BrowserCommands.duplicateTab(), () => duplicateTabIn(gBrowser.selectedTab, "tab"))),
            sep(),
            item("Cut\tCtrl+X", () => goDoCommand("cmd_cut")),
            item("Copy\tCtrl+C", () => goDoCommand("cmd_copy")),
            item("Paste\tCtrl+V", () => goDoCommand("cmd_paste")),
            sep(),
            item("Save As...\tCtrl+S", () => doCmd("Browser:SavePage") || saveBrowser(b())),
            item("Send Page by E-mail...", () => self.mailto(`?subject=${encodeURIComponent(gBrowser.selectedTab.label)}&body=${encodeURIComponent(gBrowser.selectedTab.label + "\n" + gBrowser.currentURI.spec)}`)),
            item("Send Link by E-mail...", () => self.mailto(`?subject=${encodeURIComponent(gBrowser.selectedTab.label)}&body=${encodeURIComponent(gBrowser.currentURI.spec)}`)),
            sep(),
            submenu("Zoom", [
              item("Zoom In\tCtrl++", () => FullZoom.enlarge()),
              item("Zoom Out\tCtrl+-", () => FullZoom.reduce()),
              sep(),
              ...[4, 2, 1.5, 1.25, 1, 0.75, 0.5].map(z => item(`${Math.round(z * 100)}%`, () => self.setZoom(z))),
              sep(),
              item("Custom...", () => self.customZoom()),
            ]),
            submenu(
              "Text Size",
              [
                ["Largest", 1.5],
                ["Larger", 1.25],
                ["Medium", 1],
                ["Smaller", 0.85],
                ["Smallest", 0.7],
              ].map(([label, z]) =>
                item(label, () => (b().browsingContext.textZoom = z), {
                  type: "radio",
                  checked: Math.abs((b().browsingContext?.textZoom || 1) - z) < 0.01,
                })
              )
            ),
            submenu("Encoding", [item("Auto-Select / Repair Text Encoding", () => call(() => BrowserCommands.forceEncodingDetection(), () => BrowserForceEncodingDetection()))]),
            sep(),
            submenu("Style", [
              item("No Style", () => gPageStyleMenu.disableStyle()),
              item("Default Style", () => gPageStyleMenu.switchStyleSheet("")),
            ]),
            item("Caret Browsing\tF7", () => self.togglePref("accessibility.browsewithcaret"), {
              type: "checkbox",
              checked: pref("accessibility.browsewithcaret", false),
            }),
            sep(),
            item("Web Page Privacy Policy...", () => self.openSiteInfo()),
            item("Properties", () => call(() => BrowserCommands.pageInfo(), () => BrowserPageInfo())),
            item("View Source\tCtrl+U", () => call(() => BrowserCommands.viewSource(b()), () => BrowserViewSource(b()))),
            item("Developer Tools\tF12", () => self.toggleDevTools()),
          ];
        },
        safety() {
          const sbOn = pref("browser.safebrowsing.phishing.enabled", true);
          return [
            item("Delete Browsing History...\tCtrl+Shift+Del", () => self.openDialog("delete-history")),
            item("InPrivate Browsing\tCtrl+Shift+P", () => OpenBrowserWindow({ private: true })),
            item("Tracking Protection...", () => openPreferences("privacy-trackingprotection")),
            item("Web Page Privacy Policy...", () => self.openSiteInfo()),
            sep(),
            submenu("SmartScreen Filter", [
              item("Report Unsafe Website...", () =>
                self.openURL("https://safebrowsing.google.com/safebrowsing/report_phish/?url=" + encodeURIComponent(gBrowser.currentURI.spec), "tab")
              ),
              item(sbOn ? "Turn Off SmartScreen Filter..." : "Turn On SmartScreen Filter...", () => {
                for (const p of ["browser.safebrowsing.phishing.enabled", "browser.safebrowsing.malware.enabled"]) {
                  Services.prefs.setBoolPref(p, !sbOn);
                }
              }),
            ]),
            item("Check for ValcoNet Updates", () => self.openURL("https://github.com/Raidenplays/valconet/releases", "tab")),
          ];
        },
        tools() {
          const popupsBlocked = pref("dom.disable_open_during_load", true);
          return [
            item("Diagnose Connection Problems...", () => self.launchExternal("ms-settings:network-status")),
            item("Reopen Last Browsing Session", () => SessionStore.restoreLastSession(), { disabled: !SessionStore.canRestoreLastSession }),
            sep(),
            submenu("Pop-up Blocker", [
              item(popupsBlocked ? "Turn Off Pop-up Blocker" : "Turn On Pop-up Blocker", () =>
                Services.prefs.setBoolPref("dom.disable_open_during_load", !popupsBlocked)
              ),
              item("Pop-up Blocker Settings", () => openPreferences("privacy-permissions")),
            ]),
            item("Manage Add-ons", () => call(() => BrowserAddonUI.openAddonsMgr(), () => BrowserOpenAddonsMgr())),
            self.syncMenu(),
            sep(),
            item("Work Offline", () => BrowserOffline.toggleOfflineStatus(), { type: "checkbox", checked: Services.io.offline }),
            item("View Downloads\tCtrl+J", () => call(() => BrowserCommands.downloadsUI(), () => BrowserDownloadsUI())),
            item("Full Screen\tF11", () => call(() => BrowserCommands.fullScreen(), () => BrowserFullScreen())),
            submenu("Toolbars", [
              item("Menu Bar", () => self.togglePref("valconet.menubar.alwaysShow"), { type: "checkbox", checked: pref("valconet.menubar.alwaysShow", false) }),
              item("Favorites Bar", () => self.toggleFavoritesBar(), { type: "checkbox", checked: self.favoritesBarVisible() }),
              item("Command Bar Text Labels", () => self.togglePref("valconet.commandbar.labels"), { type: "checkbox", checked: pref("valconet.commandbar.labels", true) }),
              item("Status Bar", () => self.togglePref("valconet.statusbar.visible"), { type: "checkbox", checked: pref("valconet.statusbar.visible", true) }),
              sep(),
              item("Customize...", () => gCustomizeMode.enter()),
            ]),
            submenu("Explorer Bars", [
              item("Favorites\tCtrl+Shift+I", () => self.openFavoritesCenter("favorites", true)),
              item("History\tCtrl+Shift+H", () => self.openFavoritesCenter("history", true)),
              item("Feeds\tCtrl+Shift+J", () => self.openFavoritesCenter("feeds", true)),
            ]),
            submenu("Tab Groups", [
              item("Color-code Tab Groups", () => self.togglePref("valconet.tabgroups.colors"), { type: "checkbox", checked: pref("valconet.tabgroups.colors", true) }),
              item("Ungroup All Tabs", () => self.ungroupTabs()),
            ]),
            sep(),
            item("Developer Tools\tF12", () => self.toggleDevTools()),
            item("Internet Options", () => self.openDialog("options")),
          ];
        },
        help() {
          return [
            item("ValcoNet Help\tF1", () => self.openURL("https://github.com/Raidenplays/valconet#readme", "tab")),
            item("What's New in ValcoNet", () => self.openURL("https://github.com/Raidenplays/valconet/releases", "tab")),
            item("Online Support", () => self.openURL("https://github.com/Raidenplays/valconet/issues", "tab")),
            item("Troubleshooting Information", () => self.openURL("about:support", "tab")),
            sep(),
            item("About ValcoNet", () => self.openDialog("about")),
          ];
        },
      };
    },

    /** Mozilla account sync (favorites, passwords, add-ons, history, open tabs, settings). */
    syncState() {
      try {
        const { UIState } = ChromeUtils.importESModule("resource://services-sync/UIState.sys.mjs");
        return UIState.get();
      } catch (ex) {
        return { status: "not_configured" };
      }
    },
    syncSignIn() {
      call(() => gSync.openFxAEmailFirstPage("valconet"), () => openPreferences("sync"));
    },
    syncMenu() {
      const st = this.syncState();
      const signedIn = st.status === "signed_in";
      const items = [];
      if (st.status === "not_configured") {
        items.push(
          item("Sign In to Sync...", () => this.syncSignIn()),
          item("Sync favorites, passwords, add-ons, history and open tabs with a Mozilla account.", null, { disabled: "true" })
        );
      } else if (st.status === "login_failed" || st.status === "not_verified") {
        items.push(
          item(`${st.email || "Your account"} needs attention`, null, { disabled: "true" }),
          item(st.status === "not_verified" ? "Verify Your Account..." : "Sign In Again...", () => call(() => gSync.openSignInAgainPage("valconet"), () => this.syncSignIn()))
        );
      } else {
        items.push(item(`Signed in as ${st.email || "your Mozilla account"}`, null, { disabled: "true" }));
        if (signedIn && st.syncEnabled === false) {
          items.push(item("Turn On Sync...", () => call(() => gSync.openSyncSetup?.("valconet"), () => openPreferences("sync"))));
        } else {
          items.push(item("Sync Now", () => gSync.doSync(), { disabled: st.syncing ? "true" : null }));
        }
      }
      if (st.status !== "not_configured") {
        items.push(
          sep(),
          item("Choose What to Sync...", () => call(() => gSync.openChooseWhatToSync?.("valconet"), () => openPreferences("sync"))),
          item("Manage Account...", () => gSync.openFxAManagePage("valconet")),
          item("Manage Devices...", () => call(() => gSync.openDevicesManagementPage("valconet"), () => openPreferences("sync"))),
          sep(),
          item("Sign Out...", () => gSync.disconnect({ confirm: true }))
        );
      }
      items.push(sep(), item("Sync Settings", () => openPreferences("sync")));
      return submenu("Sync", items);
    },

    toggleDevTools() {
      const k = $("key_toggleToolbox") || $("key_toggleToolboxF12");
      if (k) {
        k.doCommand();
        return;
      }
      try {
        const { gDevToolsBrowser } = ChromeUtils.importESModule("resource://devtools/client/framework/devtools-browser.sys.mjs");
        gDevToolsBrowser.toggleToolboxCommand(gBrowser, Cu.now());
      } catch (ex) {
        console.error(ex);
      }
    },

    // --------------------------------------------------- history / tab lists
    fillHistoryMenu(popup) {
      popup.replaceChildren();
      const sh = gBrowser.selectedBrowser.browsingContext?.sessionHistory;
      if (!sh || sh.count < 1) {
        popup.append(item("(no recent pages)", null, { disabled: "true" }));
        return;
      }
      const index = sh.index;
      const start = Math.max(0, index - 7);
      const end = Math.min(sh.count - 1, index + 7);
      for (let i = end; i >= start; i--) {
        const entry = sh.getEntryAtIndex(i);
        const mi = item(entry.title || entry.URI.spec, () => gBrowser.gotoIndex(i), {
          type: i === index ? "radio" : null,
          checked: i === index,
          class: "menuitem-iconic",
        });
        try {
          mi.setAttribute("image", "page-icon:" + entry.URI.spec);
        } catch (ex) {}
        popup.append(mi);
      }
      popup.append(sep(), item("History\tCtrl+Shift+H", () => this.openFavoritesCenter("history", true)));
    },

    fillTabList(popup) {
      popup.replaceChildren(
        ...gBrowser.visibleTabs.map(tab =>
          item(tab.label, () => (gBrowser.selectedTab = tab), {
            class: "menuitem-iconic",
            image: tab.image || "chrome://valconet/skin/icons/page-generic.svg",
            type: tab.selected ? "radio" : null,
            checked: tab.selected,
          })
        )
      );
    },

    // ------------------------------------------------------------ navigation
    refresh(e) {
      call(() => BrowserCommands.reloadOrDuplicate(e), () => BrowserReloadOrDuplicate(e));
    },
    stop() {
      call(() => BrowserCommands.stop(), () => BrowserStop());
    },
    openURL(url, where = "current") {
      openTrustedLinkIn(url, where, {
        triggeringPrincipal: Services.scriptSecurityManager.getSystemPrincipal(),
      });
    },
    prettyURL(url) {
      if (url === "chrome://valconet/content/home.html") {
        return "ValcoNet Home";
      }
      if (url === "about:blank") {
        return "Blank Page";
      }
      return url.replace(/^https?:\/\//, "").replace(/\/$/, "");
    },
    homePages() {
      return pref("browser.startup.homepage", "about:home").split("|").filter(Boolean);
    },
    removeHomePage(url) {
      const pages = url ? this.homePages().filter(u => u !== url) : [];
      if (!url && !Services.prompt.confirm(window, "Delete Home Page", "Are you sure you want to delete all of your home pages?")) {
        return;
      }
      Services.prefs.setStringPref("browser.startup.homepage", pages.length ? pages.join("|") : "about:blank");
    },
    mailto(query) {
      this.launchExternal("mailto:" + query);
    },
    launchExternal(url) {
      try {
        const uri = Services.io.newURI(url);
        Cc["@mozilla.org/uriloader/external-protocol-service;1"]
          .getService(Ci.nsIExternalProtocolService)
          .loadURI(uri, Services.scriptSecurityManager.getSystemPrincipal());
      } catch (ex) {
        console.error(ex);
      }
    },
    /** IE's "Read Mail" starts the default mail client (Valco Express, if it's the default). */
    readMail() {
      if (AppConstants.platform === "win") {
        try {
          const key = Cc["@mozilla.org/windows-registry-key;1"].createInstance(Ci.nsIWindowsRegKey);
          let client = null;
          for (const root of [key.ROOT_KEY_CURRENT_USER, key.ROOT_KEY_LOCAL_MACHINE]) {
            try {
              key.open(root, "SOFTWARE\\Clients\\Mail", key.ACCESS_READ);
              client = key.readStringValue("");
              key.close();
              if (client) {
                break;
              }
            } catch (ex) {}
          }
          if (client) {
            key.open(key.ROOT_KEY_LOCAL_MACHINE, `SOFTWARE\\Clients\\Mail\\${client}\\shell\\open\\command`, key.ACCESS_READ);
            const cmd = key.readStringValue("");
            key.close();
            const m = cmd.match(/^"([^"]+)"\s*(.*)$/) || cmd.match(/^(\S+)\s*(.*)$/);
            const file = Cc["@mozilla.org/file/local;1"].createInstance(Ci.nsIFile);
            file.initWithPath(m[1].replace(/%([^%]+)%/g, (_, v) => Services.env.get(v) || ""));
            const proc = Cc["@mozilla.org/process/util;1"].createInstance(Ci.nsIProcess);
            proc.init(file);
            const args = m[2] ? m[2].split(/\s+/).filter(Boolean) : [];
            proc.runw(false, args, args.length);
            return;
          }
        } catch (ex) {
          console.warn("ValcoNet: no mail client in registry", ex);
        }
      }
      this.mailto("");
    },

    // ------------------------------------------------------------ favorites
    favoritesBarVisible() {
      const tb = $("PersonalToolbar");
      return tb && !tb.collapsed;
    },
    toggleFavoritesBar() {
      const show = !this.favoritesBarVisible();
      Services.prefs.setStringPref("browser.toolbars.bookmarks.visibility", show ? "always" : "never");
      try {
        setToolbarVisibility($("PersonalToolbar"), show ? "always" : "never");
      } catch (ex) {}
    },
    async addToFavoritesBar() {
      const uri = gBrowser.currentURI;
      if (!/^(https?|file|chrome):/.test(uri.spec)) {
        return;
      }
      await PlacesUtils.bookmarks.insert({
        parentGuid: PlacesUtils.bookmarks.toolbarGuid,
        url: uri.spec,
        title: gBrowser.selectedTab.label || uri.spec,
      });
    },
    addFavorite() {
      this.openDialog("add-favorite", {
        url: gBrowser.currentURI.spec,
        title: gBrowser.selectedTab.label,
      });
    },

    favFrame() {
      const frame = $("valco-favcenter-frame");
      if (frame && !frame.getAttribute("src")) {
        frame.setAttribute("src", "chrome://valconet/content/favcenter.html");
      }
      return frame;
    },
    toggleFavoritesCenter() {
      const fav = $("valco-favcenter");
      if (fav.hidden) {
        this.openFavoritesCenter();
      } else {
        this.closeFavoritesCenter(false, true);
      }
    },
    openFavoritesCenter(section, pin) {
      const fav = $("valco-favcenter");
      const frame = this.favFrame();
      fav.hidden = false;
      if (pin) {
        this.setFavoritesPinned(true);
      }
      $("valco-favorites-button")?.setAttribute("checked", "true");
      this.pendingFavSection = section || this.pendingFavSection || "favorites";
      // If the page isn't up yet, favcenter.js picks up pendingFavSection itself when it loads.
      const fc = frame.contentWindow?.FavCenter;
      if (fc) {
        fc.show(section);
        frame.contentWindow.focus();
      }
    },
    closeFavoritesCenter(onlyFlyout = false, force = false) {
      const fav = $("valco-favcenter");
      if (!fav || fav.hidden) {
        return;
      }
      if (fav.hasAttribute("pinned") && !force) {
        return;
      }
      if (onlyFlyout && fav.hasAttribute("pinned")) {
        return;
      }
      fav.hidden = true;
      this.setFavoritesPinned(false);
      $("valco-favorites-button")?.removeAttribute("checked");
    },
    setFavoritesPinned(pinned) {
      const fav = $("valco-favcenter");
      fav.toggleAttribute("pinned", pinned);
      $("valco-favcenter-splitter").hidden = !pinned;
      try {
        this.favFrame().contentWindow?.FavCenter?.setPinned(pinned);
      } catch (ex) {}
    },

    // ------------------------------------------------------------ quick tabs
    async toggleQuickTabs() {
      const qt = $("valco-quicktabs");
      if (!qt.hidden) {
        this.closeQuickTabs();
        return;
      }
      $("valco-quicktabs-button")?.setAttribute("checked", "true");
      qt.hidden = false;
      qt.replaceChildren();
      const grid = html("div", { id: "valco-qt-grid" });
      qt.append(grid);
      const tabs = gBrowser.visibleTabs;
      const cols = Math.ceil(Math.sqrt(tabs.length));
      grid.style.setProperty("--qt-cols", cols);
      for (const tab of tabs) {
        const canvas = html("canvas", { class: "valco-qt-thumb", width: "320", height: "200" });
        const close = html("button", { class: "valco-qt-close", title: "Close Tab", onclick: e => {
          e.stopPropagation();
          gBrowser.removeTab(tab, { animate: false });
          cell.remove();
          if (!gBrowser.visibleTabs.length) {
            this.closeQuickTabs();
          }
        } });
        const cell = html("div", { class: "valco-qt-cell", selected: tab.selected ? "true" : null, onclick: () => {
          gBrowser.selectedTab = tab;
          this.closeQuickTabs();
        } }, [
          html("div", { class: "valco-qt-frame" }, [canvas, close]),
          html("div", { class: "valco-qt-title" }, [
            html("img", { class: "valco-qt-icon", src: tab.image || "chrome://valconet/skin/icons/page-generic.svg" }),
            html("span", { text: tab.label }),
          ]),
        ]);
        if (tab.getAttribute("valco-group") !== null) {
          cell.setAttribute("valco-group", tab.getAttribute("valco-group"));
        }
        grid.append(cell);
        this.paintThumb(tab, canvas);
      }
      qt.addEventListener("click", e => {
        if (e.target === qt || e.target === grid) {
          this.closeQuickTabs();
        }
      });
    },
    async paintThumb(tab, canvas) {
      const ctx = canvas.getContext("2d");
      ctx.fillStyle = "#fff";
      ctx.fillRect(0, 0, canvas.width, canvas.height);
      try {
        const browser = tab.linkedBrowser;
        if (!tab.linkedPanel || browser.isRemoteBrowser === undefined) {
          throw new Error("not loaded");
        }
        await PageThumbs.captureToCanvas(browser, canvas, { fullViewport: false });
      } catch (ex) {
        // Unloaded tabs: draw a page with the favicon, like IE did for tabs still loading.
        ctx.fillStyle = "#f3f6fb";
        ctx.fillRect(0, 0, canvas.width, canvas.height);
        const img = new Image();
        img.onload = () => ctx.drawImage(img, canvas.width / 2 - 24, canvas.height / 2 - 24, 48, 48);
        img.src = tab.image || "chrome://valconet/skin/icons/page-generic.svg";
      }
    },
    closeQuickTabs() {
      const qt = $("valco-quicktabs");
      qt.hidden = true;
      qt.replaceChildren();
      $("valco-quicktabs-button")?.removeAttribute("checked");
    },

    // --------------------------------------------------------------- dialogs
    openDialog(name, args = {}) {
      const existing = Services.wm.getMostRecentWindow("valconet:" + name);
      if (existing && name !== "add-favorite") {
        existing.focus();
        return existing;
      }
      const w = window.openDialog(
        `chrome://valconet/content/dialogs/${name}.html`,
        "_blank",
        "chrome,titlebar,dialog=yes,centerscreen,resizable=no",
        { ...args, opener: window }
      );
      return w;
    },
  };

  return VN;
})();
