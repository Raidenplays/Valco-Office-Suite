/* Chrome side of feed detection: hands the list to the window's ValcoNet layer. */
export class ValcoFeedsParent extends JSWindowActorParent {
  receiveMessage(msg) {
    if (msg.name !== "ValcoFeeds:Found" || !Services.prefs.getBoolPref("valconet.feeds.detect", true)) {
      return;
    }
    const browser = this.browsingContext.top.embedderElement;
    const win = browser?.ownerDocument?.defaultView;
    if (!win?.ValcoNetWindow) {
      return;
    }
    const page = browser.currentURI?.spec || msg.data.page;
    const feeds = (msg.data.feeds || []).slice(0, 20).map(f => ({
      href: String(f.href),
      title: String(f.title || ""),
      type: String(f.type || ""),
      page,
    }));
    win.ValcoNetWindow.onFeeds(browser, feeds);
  }
}
