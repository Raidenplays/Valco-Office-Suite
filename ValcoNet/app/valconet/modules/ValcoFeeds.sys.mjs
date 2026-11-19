/* Feed subscriptions (IE's "Common Feed List"), stored in the profile's prefs. */
const PREF = "valconet.feeds";

export const ValcoFeeds = {
  list() {
    try {
      const list = JSON.parse(Services.prefs.getStringPref(PREF, "[]"));
      return Array.isArray(list) ? list.filter(f => f && /^(https?|file):/.test(f.url)) : [];
    } catch (ex) {
      return [];
    }
  },
  save(list) {
    Services.prefs.setStringPref(PREF, JSON.stringify(list));
    Services.obs.notifyObservers(null, "valconet-feeds-changed");
  },
  isSubscribed(url) {
    return this.list().some(f => f.url === url);
  },
  subscribe(url, title) {
    if (!/^(https?|file):/.test(url) || this.isSubscribed(url)) {
      return;
    }
    const list = this.list();
    list.push({ url, title: title || url, added: Date.now() });
    this.save(list);
  },
  unsubscribe(url) {
    this.save(this.list().filter(f => f.url !== url));
  },
  rename(url, title) {
    this.save(this.list().map(f => (f.url === url ? { ...f, title } : f)));
  },

  /** Fetch and parse RSS 2.0 / RSS 1.0 / Atom / JSON Feed into plain text items. */
  async fetch(url, win) {
    const res = await win.fetch(url, { credentials: "omit", cache: "no-cache" });
    if (!res.ok) {
      throw new Error(`The feed server answered ${res.status} ${res.statusText}`);
    }
    const text = await res.text();
    return this.parse(text, url, win);
  },

  parse(text, baseURL, win) {
    const DOMParser = win.DOMParser;
    const clean = s => {
      if (!s) {
        return "";
      }
      // Descriptions are HTML: parse inertly and keep only text. Nothing from the feed is ever rendered as markup.
      const doc = new DOMParser().parseFromString(String(s), "text/html");
      doc.querySelectorAll("script, style, noscript, template").forEach(n => n.remove());
      return (doc.body?.textContent || "").replace(/\s+/g, " ").trim();
    };
    const safeLink = href => {
      try {
        const u = new URL(href, baseURL);
        return /^https?:$/.test(u.protocol) ? u.href : null;
      } catch (ex) {
        return null;
      }
    };
    const trimmed = text.trimStart();
    if (trimmed.startsWith("{")) {
      const j = JSON.parse(trimmed);
      return {
        title: clean(j.title) || baseURL,
        link: safeLink(j.home_page_url),
        description: clean(j.description),
        items: (j.items || []).map(it => ({
          title: clean(it.title) || "(untitled)",
          link: safeLink(it.url || it.external_url),
          date: it.date_published ? new Date(it.date_published) : null,
          author: clean(it.authors?.[0]?.name || it.author?.name),
          categories: (it.tags || []).map(clean),
          summary: clean(it.content_text || it.summary || it.content_html).slice(0, 1200),
        })),
      };
    }
    const xml = new DOMParser().parseFromString(text, "application/xml");
    if (xml.querySelector("parsererror")) {
      throw new Error("This doesn't look like a valid feed.");
    }
    const root = xml.documentElement;
    const kids = (node, name) => [...node.children].filter(c => c.localName === name);
    const kid = (node, name) => kids(node, name)[0];
    const txt = (node, name) => kid(node, name)?.textContent || "";
    const parseDate = s => {
      if (!s) {
        return null;
      }
      const d = new Date(s.trim());
      return isNaN(d) ? null : d;
    };

    if (root.localName === "feed") {
      // Atom
      const linkOf = n => {
        const links = kids(n, "link");
        const l = links.find(x => (x.getAttribute("rel") || "alternate") === "alternate") || links[0];
        return l ? safeLink(l.getAttribute("href")) : null;
      };
      return {
        title: clean(txt(root, "title")) || baseURL,
        link: linkOf(root),
        description: clean(txt(root, "subtitle")),
        items: kids(root, "entry").map(e => ({
          title: clean(txt(e, "title")) || "(untitled)",
          link: linkOf(e),
          date: parseDate(txt(e, "updated") || txt(e, "published")),
          author: clean(kid(e, "author") ? txt(kid(e, "author"), "name") : ""),
          categories: kids(e, "category").map(c => clean(c.getAttribute("term") || c.getAttribute("label"))),
          summary: clean(txt(e, "summary") || txt(e, "content")).slice(0, 1200),
        })),
      };
    }
    // RSS 2.0 (<rss><channel>) or RSS 1.0 (<rdf:RDF><channel/><item/>)
    const channel = root.localName === "rss" ? kid(root, "channel") : kid(root, "channel") || root;
    if (!channel) {
      throw new Error("This doesn't look like a valid feed.");
    }
    const itemNodes = kids(channel, "item").length ? kids(channel, "item") : kids(root, "item");
    return {
      title: clean(txt(channel, "title")) || baseURL,
      link: safeLink(txt(channel, "link")),
      description: clean(txt(channel, "description")),
      items: itemNodes.map(i => ({
        title: clean(txt(i, "title")) || "(untitled)",
        link: safeLink(txt(i, "link") || txt(i, "guid")),
        date: parseDate(txt(i, "pubDate") || txt(i, "date")),
        author: clean(txt(i, "creator") || txt(i, "author")),
        categories: kids(i, "category").map(c => clean(c.textContent)),
        summary: clean(txt(i, "description") || txt(i, "encoded")).slice(0, 1200),
      })),
    };
  },
};
