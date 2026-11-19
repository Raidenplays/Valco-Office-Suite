// Sample site used by the web build and as a "try it" project.
export const DEMO_SITE: Record<string, string> = {
  'index.html': `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Maple Lane Bakery</title>
  <link rel="stylesheet" href="css/style.css">
</head>
<body>
  <header class="site-header">
    <img src="images/logo.svg" alt="Maple Lane Bakery logo" width="56" height="56">
    <nav>
      <a href="index.html">Home</a>
      <a href="about.html">About</a>
      <a href="#menu">Menu</a>
    </nav>
  </header>

  <main>
    <section class="hero">
      <h1>Fresh bread, every morning</h1>
      <p>Small-batch sourdough, butter tarts and maple buns baked in Truro since 2009.</p>
      <a class="button" href="#menu">See today's menu</a>
    </section>

    <section id="menu" class="menu">
      <h2>Today's menu</h2>
      <ul>
        <li><strong>Country sourdough</strong> — $8</li>
        <li><strong>Maple pecan buns</strong> — $4</li>
        <li><strong>Butter tarts</strong> — $3</li>
      </ul>
    </section>
  </main>

  <footer>
    <p>&copy; 2026 Maple Lane Bakery · 12 Prince St, Truro NS</p>
  </footer>
  <script src="js/main.js"></script>
</body>
</html>
`,
  'about.html': `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>About — Maple Lane Bakery</title>
  <link rel="stylesheet" href="css/style.css">
</head>
<body>
  <header class="site-header">
    <img src="images/logo.svg" alt="Maple Lane Bakery logo" width="56" height="56">
    <nav>
      <a href="index.html">Home</a>
      <a href="about.html">About</a>
    </nav>
  </header>
  <main>
    <h1>About us</h1>
    <p>We're a two-person bakery that opens at 6am and closes when the bread runs out.</p>
  </main>
</body>
</html>
`,
  'css/style.css': `:root {
  --brand: #b5542b;
  --ink: #2b2118;
  --paper: #fdf8f1;
}

* { box-sizing: border-box; }

body {
  margin: 0;
  font-family: Georgia, "Times New Roman", serif;
  color: var(--ink);
  background: var(--paper);
}

.site-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 12px 24px;
  border-bottom: 1px solid #e8dccb;
}

.site-header nav a {
  margin-left: 18px;
  color: var(--ink);
  text-decoration: none;
}

.hero {
  padding: 64px 24px;
  text-align: center;
  background: #f4e6d4;
}

.hero h1 {
  font-size: 2.6rem;
  margin: 0 0 12px;
}

.button {
  display: inline-block;
  padding: 10px 22px;
  border-radius: 6px;
  background: var(--brand);
  color: white;
  text-decoration: none;
}

.menu {
  max-width: 640px;
  margin: 0 auto;
  padding: 32px 24px;
}

footer {
  text-align: center;
  padding: 24px;
  font-size: 0.9rem;
  color: #7a6a58;
}

@media (max-width: 600px) {
  .hero h1 { font-size: 1.8rem; }
  .site-header { flex-direction: column; }
}
`,
  'js/main.js': `// Highlights the current page in the nav
document.querySelectorAll('nav a').forEach((a) => {
  if (a.getAttribute('href') === location.pathname.split('/').pop()) {
    a.style.fontWeight = 'bold';
  }
});
`,
  'images/logo.svg': `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64">
  <circle cx="32" cy="32" r="30" fill="#b5542b"/>
  <path d="M14 38c4-12 32-12 36 0-6 6-30 6-36 0z" fill="#f4e6d4"/>
  <path d="M22 34c2-4 4-4 6 0M30 33c2-4 4-4 6 0M38 34c2-4 4-4 6 0" stroke="#b5542b" stroke-width="2" fill="none"/>
</svg>
`,
};
