import { escAttr, escText } from './htmlmap';

export interface InsertField {
  key: string;
  label: string;
  def?: string;
  kind?: 'text' | 'image' | 'page' | 'file' | 'number';
}

export interface InsertItem {
  id: string;
  name: string;
  icon: string;
  cat: 'HTML' | 'Text' | 'Media' | 'Form' | 'Layout' | 'Head';
  fields?: InsertField[];
  html: (v: Record<string, string>) => string;
}

const a = escAttr;
const t = escText;

export const INSERTS: InsertItem[] = [
  // HTML
  { id: 'div', name: 'Div', icon: '▢', cat: 'HTML', fields: [{ key: 'cls', label: 'Class', def: '' }], html: (v) => `<div${v.cls ? ` class="${a(v.cls)}"` : ''}>\n  Content for this div\n</div>` },
  { id: 'img', name: 'Image', icon: '🖼', cat: 'Media', fields: [{ key: 'src', label: 'Source', kind: 'image' }, { key: 'alt', label: 'Alt text', def: '' }], html: (v) => `<img src="${a(v.src)}" alt="${a(v.alt)}">` },
  { id: 'a', name: 'Hyperlink', icon: '🔗', cat: 'HTML', fields: [{ key: 'text', label: 'Text', def: 'Link text' }, { key: 'href', label: 'Link', kind: 'page', def: '#' }], html: (v) => `<a href="${a(v.href)}">${t(v.text)}</a>` },
  { id: 'hr', name: 'Horizontal rule', icon: '―', cat: 'HTML', html: () => `<hr>` },
  { id: 'table', name: 'Table', icon: '▦', cat: 'HTML', fields: [{ key: 'rows', label: 'Rows', def: '3', kind: 'number' }, { key: 'cols', label: 'Columns', def: '3', kind: 'number' }], html: (v) => {
    const r = Math.max(1, Math.min(50, +v.rows || 3));
    const c = Math.max(1, Math.min(20, +v.cols || 3));
    const cells = (tag: string) => Array.from({ length: c }, () => `<${tag}>&nbsp;</${tag}>`).join('');
    const body = Array.from({ length: r - 1 }, () => `    <tr>${cells('td')}</tr>`).join('\n');
    return `<table>\n  <thead>\n    <tr>${cells('th')}</tr>\n  </thead>\n  <tbody>\n${body}\n  </tbody>\n</table>`;
  } },
  { id: 'comment', name: 'Comment', icon: '💬', cat: 'HTML', fields: [{ key: 'text', label: 'Comment', def: 'Note' }], html: (v) => `<!-- ${v.text.replace(/--/g, '- -')} -->` },
  { id: 'button', name: 'Button', icon: '⏺', cat: 'Form', fields: [{ key: 'text', label: 'Label', def: 'Click me' }], html: (v) => `<button type="button">${t(v.text)}</button>` },
  { id: 'iframe', name: 'IFrame', icon: '⧉', cat: 'Media', fields: [{ key: 'src', label: 'URL', def: 'https://' }], html: (v) => `<iframe src="${a(v.src)}" width="560" height="315" loading="lazy"></iframe>` },

  // Text
  { id: 'h1', name: 'Heading 1', icon: 'H1', cat: 'Text', html: () => `<h1>Heading</h1>` },
  { id: 'h2', name: 'Heading 2', icon: 'H2', cat: 'Text', html: () => `<h2>Heading</h2>` },
  { id: 'h3', name: 'Heading 3', icon: 'H3', cat: 'Text', html: () => `<h3>Heading</h3>` },
  { id: 'p', name: 'Paragraph', icon: '¶', cat: 'Text', html: () => `<p>New paragraph</p>` },
  { id: 'ul', name: 'Unordered list', icon: '•', cat: 'Text', html: () => `<ul>\n  <li>Item one</li>\n  <li>Item two</li>\n</ul>` },
  { id: 'ol', name: 'Ordered list', icon: '1.', cat: 'Text', html: () => `<ol>\n  <li>Item one</li>\n  <li>Item two</li>\n</ol>` },
  { id: 'li', name: 'List item', icon: '–', cat: 'Text', html: () => `<li>Item</li>` },
  { id: 'blockquote', name: 'Blockquote', icon: '❝', cat: 'Text', html: () => `<blockquote>\n  <p>Quote</p>\n</blockquote>` },
  { id: 'pre', name: 'Preformatted', icon: '⌨', cat: 'Text', html: () => `<pre><code>code here</code></pre>` },
  { id: 'br', name: 'Line break', icon: '↵', cat: 'Text', html: () => `<br>` },
  { id: 'span', name: 'Span', icon: '⟨⟩', cat: 'Text', fields: [{ key: 'cls', label: 'Class', def: '' }], html: (v) => `<span${v.cls ? ` class="${a(v.cls)}"` : ''}>text</span>` },

  // Layout
  { id: 'header', name: 'Header', icon: '▔', cat: 'Layout', html: () => `<header>\n  <h1>Site title</h1>\n</header>` },
  { id: 'nav', name: 'Navigation', icon: '☰', cat: 'Layout', html: () => `<nav>\n  <a href="#">Home</a>\n  <a href="#">About</a>\n  <a href="#">Contact</a>\n</nav>` },
  { id: 'main', name: 'Main', icon: '▣', cat: 'Layout', html: () => `<main>\n  <p>Main content</p>\n</main>` },
  { id: 'section', name: 'Section', icon: '§', cat: 'Layout', html: () => `<section>\n  <h2>Section title</h2>\n  <p>Section content</p>\n</section>` },
  { id: 'article', name: 'Article', icon: '📰', cat: 'Layout', html: () => `<article>\n  <h2>Article title</h2>\n  <p>Article content</p>\n</article>` },
  { id: 'aside', name: 'Aside', icon: '▐', cat: 'Layout', html: () => `<aside>\n  <p>Side content</p>\n</aside>` },
  { id: 'footer', name: 'Footer', icon: '▁', cat: 'Layout', html: () => `<footer>\n  <p>&copy; 2026</p>\n</footer>` },
  { id: 'figure', name: 'Figure', icon: '🖾', cat: 'Layout', fields: [{ key: 'src', label: 'Image', kind: 'image' }, { key: 'cap', label: 'Caption', def: 'Caption' }], html: (v) => `<figure>\n  <img src="${a(v.src)}" alt="${a(v.cap)}">\n  <figcaption>${t(v.cap)}</figcaption>\n</figure>` },
  { id: 'flex', name: 'Flex row', icon: '⫼', cat: 'Layout', html: () => `<div style="display:flex; gap:16px;">\n  <div>Column 1</div>\n  <div>Column 2</div>\n  <div>Column 3</div>\n</div>` },
  { id: 'grid', name: 'Grid (2 col)', icon: '⊞', cat: 'Layout', html: () => `<div style="display:grid; grid-template-columns:1fr 1fr; gap:16px;">\n  <div>Cell 1</div>\n  <div>Cell 2</div>\n</div>` },

  // Media
  { id: 'video', name: 'Video', icon: '🎞', cat: 'Media', fields: [{ key: 'src', label: 'Source', kind: 'file' }], html: (v) => `<video src="${a(v.src)}" controls width="640"></video>` },
  { id: 'audio', name: 'Audio', icon: '🔊', cat: 'Media', fields: [{ key: 'src', label: 'Source', kind: 'file' }], html: (v) => `<audio src="${a(v.src)}" controls></audio>` },
  { id: 'youtube', name: 'YouTube embed', icon: '▶', cat: 'Media', fields: [{ key: 'id', label: 'Video URL or ID', def: '' }], html: (v) => {
    const m = v.id.match(/(?:v=|youtu\.be\/|embed\/)([\w-]{6,})/);
    const id = m ? m[1] : v.id.trim();
    return `<iframe width="560" height="315" src="https://www.youtube.com/embed/${a(id)}" title="YouTube video" allowfullscreen></iframe>`;
  } },

  // Form
  { id: 'form', name: 'Form', icon: '📋', cat: 'Form', html: () => `<form action="#" method="post">\n  <label for="name">Name</label>\n  <input type="text" id="name" name="name">\n  <button type="submit">Send</button>\n</form>` },
  { id: 'input', name: 'Text field', icon: '⎕', cat: 'Form', fields: [{ key: 'name', label: 'Name', def: 'field' }], html: (v) => `<label for="${a(v.name)}">${t(v.name)}</label>\n<input type="text" id="${a(v.name)}" name="${a(v.name)}">` },
  { id: 'email', name: 'Email field', icon: '@', cat: 'Form', html: () => `<label for="email">Email</label>\n<input type="email" id="email" name="email">` },
  { id: 'textarea', name: 'Text area', icon: '▤', cat: 'Form', html: () => `<textarea name="message" rows="5" cols="40"></textarea>` },
  { id: 'checkbox', name: 'Checkbox', icon: '☑', cat: 'Form', html: () => `<label><input type="checkbox" name="option"> Option</label>` },
  { id: 'radio', name: 'Radio group', icon: '◉', cat: 'Form', html: () => `<label><input type="radio" name="choice" value="a"> Choice A</label>\n<label><input type="radio" name="choice" value="b"> Choice B</label>` },
  { id: 'select', name: 'Select', icon: '▾', cat: 'Form', html: () => `<select name="choice">\n  <option>Option 1</option>\n  <option>Option 2</option>\n</select>` },
  { id: 'submit', name: 'Submit button', icon: '⏎', cat: 'Form', html: () => `<button type="submit">Submit</button>` },

  // Head
  { id: 'meta-desc', name: 'Meta description', icon: 'ⓜ', cat: 'Head', fields: [{ key: 'd', label: 'Description', def: '' }], html: (v) => `<meta name="description" content="${a(v.d)}">` },
  { id: 'css-link', name: 'Stylesheet link', icon: '#', cat: 'Head', fields: [{ key: 'href', label: 'CSS file', kind: 'file', def: 'css/style.css' }], html: (v) => `<link rel="stylesheet" href="${a(v.href)}">` },
  { id: 'script', name: 'Script', icon: '{}', cat: 'Head', fields: [{ key: 'src', label: 'JS file (blank = inline)', kind: 'file', def: '' }], html: (v) => (v.src ? `<script src="${a(v.src)}"></script>` : `<script>\n  // code\n</script>`) },
  { id: 'favicon', name: 'Favicon', icon: '★', cat: 'Head', fields: [{ key: 'href', label: 'Icon', kind: 'image' }], html: (v) => `<link rel="icon" href="${a(v.href)}">` },
];

export const INSERT_CATS: InsertItem['cat'][] = ['HTML', 'Text', 'Layout', 'Media', 'Form', 'Head'];
