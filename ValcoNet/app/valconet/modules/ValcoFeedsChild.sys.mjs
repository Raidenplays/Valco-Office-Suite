/* Content side of feed detection: reports <link rel="alternate"> RSS/Atom feeds. */
const FEED_TYPES = new Set([
  "application/rss+xml",
  "application/atom+xml",
  "application/rdf+xml",
  "application/feed+json",
  "text/xml",
  "application/xml",
]);

export class ValcoFeedsChild extends JSWindowActorChild {
  handleEvent() {
    this.sendAsyncMessage("ValcoFeeds:Found", this.scan());
  }

  receiveMessage(msg) {
    if (msg.name === "ValcoFeeds:Scan") {
      return this.scan();
    }
    return null;
  }

  scan() {
    const doc = this.document;
    if (!doc) {
      return { feeds: [], page: "" };
    }
    const feeds = [];
    const seen = new Set();
    for (const link of doc.querySelectorAll("link[rel][href]")) {
      const rels = link.rel.toLowerCase().split(/\s+/);
      const type = (link.type || "").toLowerCase().split(";")[0].trim();
      if (!rels.includes("alternate") || rels.includes("stylesheet") || !FEED_TYPES.has(type)) {
        continue;
      }
      if ((type === "text/xml" || type === "application/xml") && !/rss|atom|feed/i.test(link.title + link.href)) {
        continue;
      }
      let href;
      try {
        href = new URL(link.getAttribute("href"), doc.baseURI).href;
      } catch (ex) {
        continue;
      }
      if (!/^(https?|file):/.test(href) || seen.has(href)) {
        continue;
      }
      seen.add(href);
      feeds.push({ href, title: (link.title || "").trim().slice(0, 200), type });
    }
    return { feeds, page: doc.documentURI };
  }
}
