import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import App from './App';
import { WindowFrame } from './components/WindowFrame';
import './styles.css';
import './themes.css';

// Put the saved theme on <html> before the first paint, so the window frame starts right.
try {
  const p = JSON.parse(localStorage.getItem('weavecraft.prefs') || '{}');
  const fresh = p.suite !== 1;
  document.documentElement.dataset.theme = fresh ? 'vista' : p.theme || 'vista';
  document.documentElement.dataset.mode = fresh ? 'light' : p.mode === 'dark' ? 'dark' : p.mode === 'light' ? 'light' : matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
} catch { /* defaults */ }

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <WindowFrame>
      <App />
    </WindowFrame>
  </StrictMode>,
);
