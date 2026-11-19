export interface DocTemplate {
  id: string;
  name: string;
  description: string;
  ext: string;
  /** Extra files created next to the page (relative path -> contents), only if missing. */
  extra?: Record<string, string>;
  body: (title: string) => string;
}

const head = (title: string, extra = '') => `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>${title}</title>${extra}
</head>`;

export const TEMPLATES: DocTemplate[] = [
  {
    id: 'blank',
    name: 'Blank HTML5',
    description: 'Empty HTML5 page',
    ext: 'html',
    body: (t) => `${head(t)}
<body>

</body>
</html>
`,
  },
  {
    id: 'responsive',
    name: 'Responsive starter',
    description: 'Header, nav, main, sidebar, footer with a linked stylesheet',
    ext: 'html',
    extra: {
      'css/layout.css': `* { box-sizing: border-box; }
body { margin: 0; font-family: system-ui, sans-serif; line-height: 1.6; color: #222; }
.site-header, .site-footer { padding: 16px 24px; background: #1f2933; color: #fff; }
.site-header nav a { color: #cbd2d9; margin-right: 16px; text-decoration: none; }
.layout { display: grid; grid-template-columns: 1fr 280px; gap: 24px; max-width: 1100px; margin: 0 auto; padding: 24px; }
.sidebar { background: #f5f7fa; padding: 16px; border-radius: 8px; }
@media (max-width: 768px) {
  .layout { grid-template-columns: 1fr; }
}
`,
    },
    body: (t) => `${head(t, '\n  <link rel="stylesheet" href="css/layout.css">')}
<body>
  <header class="site-header">
    <h1>${t}</h1>
    <nav>
      <a href="#">Home</a>
      <a href="#">About</a>
      <a href="#">Contact</a>
    </nav>
  </header>
  <div class="layout">
    <main>
      <h2>Welcome</h2>
      <p>Start writing your content here.</p>
    </main>
    <aside class="sidebar">
      <h3>Sidebar</h3>
      <p>Links, news or ads go here.</p>
    </aside>
  </div>
  <footer class="site-footer">
    <p>&copy; 2026 ${t}</p>
  </footer>
</body>
</html>
`,
  },
  {
    id: 'bootstrap',
    name: 'Bootstrap 5 starter',
    description: 'Navbar, hero and cards using Bootstrap from a CDN',
    ext: 'html',
    body: (t) => `${head(t, '\n  <link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/css/bootstrap.min.css">')}
<body>
  <nav class="navbar navbar-expand-lg navbar-dark bg-dark">
    <div class="container">
      <a class="navbar-brand" href="#">${t}</a>
    </div>
  </nav>
  <div class="container py-5">
    <div class="p-5 mb-4 bg-light rounded-3">
      <h1 class="display-5 fw-bold">Hello, world</h1>
      <p class="col-md-8 fs-5">A Bootstrap page made in Valco Weave.</p>
      <a class="btn btn-primary btn-lg" href="#">Get started</a>
    </div>
    <div class="row g-4">
      <div class="col-md-4"><div class="card"><div class="card-body"><h5 class="card-title">Card one</h5><p class="card-text">Some text.</p></div></div></div>
      <div class="col-md-4"><div class="card"><div class="card-body"><h5 class="card-title">Card two</h5><p class="card-text">Some text.</p></div></div></div>
      <div class="col-md-4"><div class="card"><div class="card-body"><h5 class="card-title">Card three</h5><p class="card-text">Some text.</p></div></div></div>
    </div>
  </div>
  <script src="https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/js/bootstrap.bundle.min.js"></script>
</body>
</html>
`,
  },
  {
    id: 'retro',
    name: '2009 personal homepage',
    description: 'Centered fixed-width page with a gradient header — classic Dreamweaver era',
    ext: 'html',
    body: (t) => `${head(t, `
  <style>
    body { background: #dfe7ef; font: 13px Verdana, Arial, sans-serif; margin: 0; }
    #wrapper { width: 900px; margin: 20px auto; background: #fff; border: 1px solid #a9b8c7; box-shadow: 0 0 12px rgba(0,0,0,.15); }
    #header { height: 110px; background: linear-gradient(#5a9bd4, #2d6aa3); color: #fff; padding: 20px 30px; }
    #nav { background: #1e4c78; padding: 8px 30px; }
    #nav a { color: #fff; margin-right: 18px; font-weight: bold; text-decoration: none; }
    #content { padding: 24px 30px; }
    #footer { border-top: 1px solid #ccc; padding: 12px 30px; color: #777; font-size: 11px; }
  </style>`)}
<body>
  <div id="wrapper">
    <div id="header">
      <h1>${t}</h1>
    </div>
    <div id="nav"><a href="#">Home</a><a href="#">Projects</a><a href="#">Guestbook</a></div>
    <div id="content">
      <h2>Welcome to my homepage!</h2>
      <p>This site is best viewed at 1024x768.</p>
    </div>
    <div id="footer">Made with Valco Weave</div>
  </div>
</body>
</html>
`,
  },
  { id: 'css', name: 'CSS stylesheet', description: 'Empty .css file', ext: 'css', body: () => `/* Styles */\n\n` },
  { id: 'js', name: 'JavaScript', description: 'Empty .js file', ext: 'js', body: () => `// Script\n\n` },
  { id: 'md', name: 'Markdown', description: 'Empty .md file', ext: 'md', body: (t) => `# ${t}\n\n` },
  { id: 'txt', name: 'Plain text', description: 'Empty .txt file', ext: 'txt', body: () => `` },
];
