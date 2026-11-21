/* ValcoNet toolbar widgets. They are real CustomizableUI widgets, so Gecko's
 * toolbar machinery (overflow, keyboard nav, customize mode) keeps working. */

import { CustomizableUI } from "moz-src:///browser/components/customizableui/CustomizableUI.sys.mjs";

const XUL = "http://www.mozilla.org/keymaster/gatekeeper/there.is.only.xul";
export const LAYOUT_VERSION = 5;

function el(doc, tag, attrs = {}, children = []) {
  const node = doc.createXULElement(tag);
  for (const [k, v] of Object.entries(attrs)) {
    if (k.startsWith("on") && typeof v === "function") {
      node.addEventListener(k.slice(2), v);
    } else if (v !== null && v !== undefined) {
      node.setAttribute(k, v);
    }
  }
  for (const c of children) {
    node.append(c);
  }
  return node;
}

const vn = doc => doc.defaultView.ValcoNetWindow;

/** A menu button: icon + label + small arrow. menuId fills from ValcoNetWindow.menus. */
function menuButton(doc, id, label, menuKey, { split = false, command = null, tooltip = null } = {}) {
  const popup = el(doc, "menupopup", {
    id: id + "-popup",
    class: "valco-menu",
    onpopupshowing: e => {
      if (e.target === e.currentTarget) {
        vn(doc).fillMenu(menuKey, e.target);
      }
    },
  });
  if (split) {
    // IE's split buttons: the icon runs the default action, the arrow opens the menu.
    const main = el(doc, "toolbarbutton", {
      id,
      class: "valco-cmd valco-cmd-main",
      label,
      tooltiptext: tooltip || label,
      oncommand: e => vn(doc).runCommand(command, e),
    });
    const arrow = el(
      doc,
      "toolbarbutton",
      { id: id + "-arrow", class: "valco-cmd valco-cmd-arrow", type: "menu", wantdropmarker: "true", tooltiptext: tooltip || label },
      [popup]
    );
    return el(doc, "hbox", { class: "valco-split", id: id + "-split" }, [main, arrow]);
  }
  return el(
    doc,
    "toolbarbutton",
    { id, class: "valco-cmd valco-cmd-menu", type: "menu", wantdropmarker: "true", label, tooltiptext: tooltip || label },
    [popup]
  );
}

const WIDGETS = [
  {
    id: "valco-history-button",
    label: "Recent Pages",
    build(doc) {
      return el(
        doc,
        "toolbarbutton",
        { id: "valco-history-button", class: "toolbarbutton-1", type: "menu", tooltiptext: "Recent Pages", removable: "true" },
        [
          el(doc, "menupopup", {
            id: "valco-history-popup",
            class: "valco-menu",
            onpopupshowing: e => vn(doc).fillHistoryMenu(e.target),
          }),
        ]
      );
    },
  },
  {
    id: "valco-refresh-stop",
    label: "Refresh / Stop",
    build(doc) {
      return el(doc, "toolbaritem", { id: "valco-refresh-stop", removable: "true", overflows: "false" }, [
        el(doc, "toolbarbutton", {
          id: "valco-refresh",
          class: "toolbarbutton-1",
          tooltiptext: "Refresh (F5)",
          oncommand: e => vn(doc).refresh(e),
        }),
        el(doc, "toolbarbutton", {
          id: "valco-stop",
          class: "toolbarbutton-1",
          tooltiptext: "Stop (Esc)",
          disabled: "true",
          oncommand: () => vn(doc).stop(),
        }),
      ]);
    },
  },
  {
    id: "valco-tabtools",
    label: "Quick Tabs",
    build(doc) {
      return el(doc, "toolbaritem", { id: "valco-tabtools", removable: "true", overflows: "false" }, [
        el(doc, "toolbarbutton", {
          id: "valco-quicktabs-button",
          class: "toolbarbutton-1",
          tooltiptext: "Quick Tabs (Ctrl+Q)",
          oncommand: () => vn(doc).toggleQuickTabs(),
        }),
        el(doc, "toolbarbutton", { id: "valco-tablist-button", class: "toolbarbutton-1", type: "menu", tooltiptext: "Tab List" }, [
          el(doc, "menupopup", {
            id: "valco-tablist-popup",
            class: "valco-menu",
            onpopupshowing: e => vn(doc).fillTabList(e.target),
          }),
        ]),
      ]);
    },
  },
  {
    id: "valco-commandbar",
    label: "Command Bar",
    build(doc) {
      return el(doc, "toolbaritem", { id: "valco-commandbar", removable: "true", overflows: "false" }, [
        menuButton(doc, "valco-cmd-home", "", "home", { split: true, command: "home", tooltip: "Home (Alt+Home)" }),
        menuButton(doc, "valco-cmd-feeds", "", "feeds", { split: true, command: "feeds", tooltip: "No Feeds Detected on this page (Alt+J)" }),
        menuButton(doc, "valco-cmd-mail", "", "mail", { split: true, command: "mail", tooltip: "Read Mail" }),
        menuButton(doc, "valco-cmd-print", "", "print", { split: true, command: "print", tooltip: "Print (Alt+R)" }),
        menuButton(doc, "valco-cmd-page", "Page", "page", { tooltip: "Page (Alt+P)" }),
        menuButton(doc, "valco-cmd-safety", "Safety", "safety", { tooltip: "Safety (Alt+S)" }),
        menuButton(doc, "valco-cmd-tools", "Tools", "tools", { tooltip: "Tools (Alt+O)" }),
        menuButton(doc, "valco-cmd-help", "", "help", { tooltip: "Help (Alt+L)" }),
      ]);
    },
  },
  {
    id: "valco-favorites",
    label: "Favorites",
    build(doc) {
      return el(doc, "toolbaritem", { id: "valco-favorites", removable: "true", overflows: "false" }, [
        el(doc, "toolbarbutton", {
          id: "valco-favorites-button",
          class: "toolbarbutton-1",
          label: "Favorites",
          tooltiptext: "View favorites, feeds, and history (Alt+C)",
          oncommand: () => vn(doc).toggleFavoritesCenter(),
        }),
        el(doc, "toolbarbutton", {
          id: "valco-addfav-button",
          class: "toolbarbutton-1",
          tooltiptext: "Add to Favorites Bar",
          oncommand: () => vn(doc).addToFavoritesBar(),
        }),
      ]);
    },
  },
];

let created = false;

export const ValcoWidgets = {
  ensure() {
    if (created) {
      return;
    }
    created = true;
    for (const w of WIDGETS) {
      try {
        CustomizableUI.createWidget({
          id: w.id,
          type: "custom",
          label: w.label,
          removable: true,
          onBuild: doc => w.build(doc),
        });
      } catch (ex) {
        console.error("ValcoNet: widget", w.id, ex);
      }
    }
  },

  /** Put every toolbar where IE8 had it. Runs once per layout version. */
  applyLayout() {
    if (Services.prefs.getIntPref("valconet.layoutVersion", 0) >= LAYOUT_VERSION) {
      return;
    }
    const want = {
      "nav-bar": [
        "back-button",
        "forward-button",
        "valco-history-button",
        "urlbar-container",
        "valco-refresh-stop",
        "search-container",
        "downloads-button",
        "unified-extensions-button",
      ],
      TabsToolbar: ["valco-tabtools", "tabbrowser-tabs", "new-tab-button", "valco-commandbar"],
      PersonalToolbar: ["valco-favorites", "personal-bookmarks"],
    };
    CustomizableUI.beginBatchUpdate();
    try {
      for (const [area, ids] of Object.entries(want)) {
        for (const id of CustomizableUI.getWidgetIdsInArea(area)) {
          if (!ids.includes(id) && CustomizableUI.isWidgetRemovable(id)) {
            CustomizableUI.removeWidgetFromArea(id);
          }
        }
        ids.forEach((id, index) => {
          try {
            const p = CustomizableUI.getPlacementOfWidget(id);
            if (p && p.area === area) {
              CustomizableUI.moveWidgetWithinArea(id, index);
            } else {
              CustomizableUI.addWidgetToArea(id, area, index);
            }
          } catch (ex) {
            // Widgets that live outside the customizable target (e.g. extensions button) are fine as-is.
          }
        });
      }
    } finally {
      CustomizableUI.endBatchUpdate();
    }
    Services.prefs.setStringPref("browser.toolbars.bookmarks.visibility", "always");
    Services.prefs.setIntPref("valconet.layoutVersion", LAYOUT_VERSION);
  },
};

export { XUL };
