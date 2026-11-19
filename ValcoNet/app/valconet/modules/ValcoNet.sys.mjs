/* ValcoNet — an Internet Explorer–style browser from the Valco suite.
 * Core module: runs once per process (from valconet.cfg) and dresses every
 * browser window in the ValcoNet chrome.
 *
 * © 2026 ItsRaiden. Licensed under the MPL 2.0, like the Gecko engine it rides on.
 */

import { setTimeout } from "resource://gre/modules/Timer.sys.mjs";

const BROWSER_URL = "chrome://browser/content/browser.xhtml";
const SHEET = "chrome://valconet/skin/valconet.css";

export const VALCONET_VERSION = "1.0.0";

export const ValcoNet = {
  started: false,

  startup() {
    if (this.started) {
      return;
    }
    this.started = true;
    this.applyDefaultPrefs();
    this.registerBranding();

    Services.obs.addObserver(this, "chrome-document-interactive");
    Services.obs.addObserver(this, "browser-delayed-startup-finished");
    Services.obs.addObserver(this, "http-on-examine-response");

    // Feed detection actor (pages advertise RSS/Atom through <link rel=alternate>).
    try {
      ChromeUtils.registerWindowActor("ValcoFeeds", {
        parent: { esModuleURI: "resource://valconet/ValcoFeedsParent.sys.mjs" },
        child: {
          esModuleURI: "resource://valconet/ValcoFeedsChild.sys.mjs",
          events: { DOMContentLoaded: {}, pageshow: { mozSystemGroup: true } },
        },
        allFrames: false,
        matches: ["http://*/*", "https://*/*", "file:///*"],
      });
    } catch (ex) {
      console.error("ValcoNet: feed actor", ex);
    }
  },

  /** ValcoNet's brand.ftl takes precedence over the engine's (later Fluent sources win). */
  registerBranding() {
    try {
      const reg = L10nRegistry.getInstance();
      if (!reg.hasSource("9-valconet")) {
        reg.registerSources([
          new L10nFileSource("9-valconet", "app", ["en-US"], "resource://valconet-l10n/{locale}/"),
        ]);
      }
    } catch (ex) {
      console.error("ValcoNet: branding", ex);
    }
  },

  /** Defaults that turn the Gecko browser into ValcoNet. Users can still change them. */
  applyDefaultPrefs() {
    const d = Services.prefs.getDefaultBranch("");
    const set = (name, value) => {
      try {
        switch (typeof value) {
          case "boolean":
            d.setBoolPref(name, value);
            break;
          case "number":
            d.setIntPref(name, value);
            break;
          default:
            d.setStringPref(name, value);
        }
      } catch (ex) {
        console.error("ValcoNet: pref", name, ex);
      }
    };

    // Frame and layout.
    set("browser.tabs.inTitlebar", 1);
    set("browser.toolbars.bookmarks.visibility", "always");
    set("browser.search.widget.inNavBar", true);
    set("browser.search.widget.new", false);
    set("sidebar.revamp", false);
    set("sidebar.verticalTabs", false);
    set("sidebar.visibility", "hide-sidebar");
    set("browser.uidensity", 0);
    set("browser.compactmode.show", false);
    set("browser.tabs.firefox-view", false);
    set("browser.tabs.tabmanager.enabled", false);
    set("browser.tabs.hoverPreview.enabled", false);
    set("browser.tabs.groups.enabled", false);
    set("browser.urlbar.trimURLs", false);
    set("browser.urlbar.trimHttps", false);
    set("browser.urlbar.unifiedSearchButton.always", false);
    set("browser.urlbar.scotchBonnet.enableOverride", false);
    set("browser.ml.chat.enabled", false);
    set("browser.ml.chat.sidebar", false);
    set("browser.download.autohideButton", true);
    set("extensions.unifiedExtensions.button.always_visible", false);
    set("widget.windows.mica", true);
    set("widget.windows.mica.toplevel-backdrop", 2); // acrylic: closest thing Windows 11 has to Aero glass
    set("browser.display.windows.non_native_menus", 1);

    // Classic, light IE look.
    set("ui.systemUsesDarkTheme", 0);
    set("browser.theme.toolbar-theme", 1);
    set("browser.theme.content-theme", 1);
    set("browser.display.use_system_colors", false);

    // Pages.
    set("browser.startup.homepage", "chrome://valconet/content/home.html");
    set("browser.startup.page", 1);
    set("browser.newtabpage.enabled", true);
    set("browser.aboutwelcome.enabled", false);
    set("browser.startup.homepage_override.mstone", "ignore");
    set("startup.homepage_welcome_url", "");
    set("startup.homepage_welcome_url.additional", "");
    set("browser.shell.checkDefaultBrowser", false);
    set("browser.privatebrowsing.vpnpromourl", "");
    set("browser.vpn_promo.enabled", false);
    set("browser.promo.focus.enabled", false);
    set("extensions.pocket.enabled", false);
    set("identity.fxaccounts.toolbar.enabled", false);
    set("browser.newtabpage.activity-stream.feeds.section.topstories", false);
    set("browser.newtabpage.activity-stream.showSponsored", false);
    set("browser.newtabpage.activity-stream.showSponsoredTopSites", false);

    // No Mozilla onboarding / Terms of Use modal in ValcoNet.
    set("browser.preonboarding.enabled", false);
    set("termsofuse.bypassNotification", true);
    set("datareporting.policy.dataSubmissionPolicyBypassNotification", true);
    set("browser.newtabpage.activity-stream.asrouter.userprefs.cfr.addons", false);
    set("browser.newtabpage.activity-stream.asrouter.userprefs.cfr.features", false);
    set("messaging-system.askForFeedback", false);

    // Telemetry off.
    set("datareporting.policy.dataSubmissionEnabled", false);
    set("datareporting.healthreport.uploadEnabled", false);
    set("toolkit.telemetry.enabled", false);
    set("toolkit.telemetry.unified", false);
    set("app.shield.optoutstudies.enabled", false);
    set("app.normandy.enabled", false);
    set("browser.discovery.enabled", false);
    set("browser.crashReports.unsubmittedCheck.enabled", false);
    set("browser.tabs.crashReporting.sendReport", false);

    // Keep the disk cache out of the roaming profile, like a normal install.
    try {
      if (Services.appinfo.OS === "WINNT") {
        const local = Services.dirsvc.get("LocalAppData", Ci.nsIFile);
        local.append("Valco");
        local.append("ValcoNet");
        local.append("Cache");
        set("browser.cache.disk.parent_directory", local.path);
      }
    } catch (ex) {}

    // ValcoNet's own settings.
    set("valconet.statusbar.visible", true);
    set("valconet.favoritesbar.visible", true);
    set("valconet.commandbar.labels", true);
    set("valconet.tabgroups.colors", true);
    set("valconet.menubar.alwaysShow", false);
    set("valconet.glass", "auto"); // auto | acrylic | painted
    set("valconet.feeds", "[]");
    set("valconet.feeds.detect", true);
    set("valconet.home.feeds", true);
    set("valconet.newtab.show", true);
  },

  observe(subject, topic) {
    switch (topic) {
      case "chrome-document-interactive": {
        const win = subject.defaultView;
        if (win && subject.documentURI === BROWSER_URL) {
          this.onBrowserDOMReady(win);
        }
        break;
      }
      case "http-on-examine-response":
        this.maybeShowFeed(subject);
        break;
      case "browser-delayed-startup-finished":
        this.onBrowserReady(subject);
        break;
    }
  },

  onBrowserDOMReady(win) {
    try {
      win.windowUtils.loadSheetUsingURIString(SHEET, win.windowUtils.AUTHOR_SHEET);
      win.document.documentElement.setAttribute("valconet", "true");
      // The script sets window.ValcoNetWindow as a real property, so modules, actors
      // and child frames can reach it.
      Services.scriptloader.loadSubScript("chrome://valconet/content/valconet-window.js", win);
      win.ValcoNetWindow.build();
    } catch (ex) {
      console.error("ValcoNet: window build failed", ex);
    }
  },

  onBrowserReady(win) {
    if (!this._firstWindowDone) {
      this._firstWindowDone = true;
      this.onFirstRun();
    }
    try {
      win.ValcoNetWindow?.init();
    } catch (ex) {
      console.error("ValcoNet: window init failed", ex);
    }
  },

  /** Like IE7/8: opening an RSS/Atom URL shows the feed view instead of downloading XML. */
  maybeShowFeed(subject) {
    try {
      const channel = subject.QueryInterface(Ci.nsIHttpChannel);
      const info = channel.loadInfo;
      if (info.externalContentPolicyType !== Ci.nsIContentPolicy.TYPE_DOCUMENT) {
        return;
      }
      let type = "";
      try {
        type = channel.getResponseHeader("Content-Type").toLowerCase();
      } catch (ex) {
        return;
      }
      if (!/^application\/(rss|atom|rdf)\+xml/.test(type) || channel.responseStatus >= 300) {
        return;
      }
      if (!Services.prefs.getBoolPref("valconet.feeds.detect", true)) {
        return;
      }
      const browser = info.browsingContext?.top?.embedderElement;
      if (!browser?.ownerDocument?.defaultView?.ValcoNetWindow) {
        return;
      }
      const url = channel.URI.spec;
      channel.cancel(Cr.NS_BINDING_ABORTED);
      Services.tm.dispatchToMainThread(() => {
        browser.loadURI(Services.io.newURI("chrome://valconet/content/feed.html?url=" + encodeURIComponent(url)), {
          triggeringPrincipal: Services.scriptSecurityManager.getSystemPrincipal(),
        });
      });
    } catch (ex) {
      // Not an HTTP document load we care about.
    }
  },

  /** Is ValcoNet the Windows default for https? Uses the ProgID that the ValcoNet MSI registers. */
  isDefaultBrowser() {
    if (Services.appinfo.OS !== "WINNT") {
      try {
        const { ShellService } = ChromeUtils.importESModule("moz-src:///browser/components/shell/ShellService.sys.mjs");
        return ShellService.isDefaultBrowser(false, false);
      } catch (ex) {
        return false;
      }
    }
    try {
      const key = Cc["@mozilla.org/windows-registry-key;1"].createInstance(Ci.nsIWindowsRegKey);
      key.open(
        key.ROOT_KEY_CURRENT_USER,
        "Software\\Microsoft\\Windows\\Shell\\Associations\\UrlAssociations\\https\\UserChoice",
        key.ACCESS_READ
      );
      const progId = key.readStringValue("ProgId");
      key.close();
      return progId === "ValcoNetURL";
    } catch (ex) {
      return false;
    }
  },

  /** Windows 11 only lets the user pick defaults; open ValcoNet's page in Default apps. */
  makeDefault() {
    const url = Services.appinfo.OS === "WINNT" ? "ms-settings:defaultapps?registeredAppMachine=ValcoNet" : null;
    if (!url) {
      return;
    }
    const uri = Services.io.newURI(url);
    Cc["@mozilla.org/uriloader/external-protocol-service;1"]
      .getService(Ci.nsIExternalProtocolService)
      .loadURI(uri, Services.scriptSecurityManager.getSystemPrincipal());
  },

  /** New tab page: the IE8 "What do you want to do next?" page. */
  async setupNewTab() {
    try {
      const { AboutNewTab } = ChromeUtils.importESModule(
        "resource:///modules/AboutNewTab.sys.mjs"
      );
      AboutNewTab.newTabURL = "chrome://valconet/content/newtab.html";
    } catch (ex) {
      console.error("ValcoNet: new tab", ex);
    }
  },

  async onFirstRun() {
    this.setupNewTab();
    if (Services.prefs.getBoolPref("valconet.firstRun.done", false)) {
      return;
    }
    // Let Places finish importing its defaults first, or it would replace what we add.
    await new Promise(resolve => {
      let finished = false;
      const done = () => {
        if (finished) {
          return;
        }
        finished = true;
        try {
          Services.obs.removeObserver(done, "places-browser-init-complete");
        } catch (ex) {}
        resolve();
      };
      Services.obs.addObserver(done, "places-browser-init-complete");
      setTimeout(done, 8000);
    }).catch(() => {});
    if (Services.prefs.getBoolPref("valconet.firstRun.done", false)) {
      return;
    }
    Services.prefs.setBoolPref("valconet.firstRun.done", true);
    try {
      const { PlacesUtils } = ChromeUtils.importESModule(
        "resource://gre/modules/PlacesUtils.sys.mjs"
      );
      const bar = PlacesUtils.bookmarks.toolbarGuid;
      const tree = await PlacesUtils.promiseBookmarksTree(bar);
      const have = new Set((tree.children || []).map(c => c.uri));
      let index = 0;
      for (const [title, url] of [
        ["ValcoNet Home", "chrome://valconet/content/home.html"],
        ["Get More Add-ons", "https://addons.mozilla.org/"],
        ["Wikipedia", "https://www.wikipedia.org/"],
      ]) {
        if (!have.has(url)) {
          await PlacesUtils.bookmarks.insert({ parentGuid: bar, title, url, index: index++ });
        }
      }
    } catch (ex) {
      console.error("ValcoNet: first run", ex);
    }
  },
};
