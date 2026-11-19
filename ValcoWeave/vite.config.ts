import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import { viteSingleFile } from 'vite-plugin-singlefile';

// `npm run build` makes the desktop frontend; `npm run build:web` makes a single-file web build.
export default defineConfig(({ mode }) => ({
  plugins: [react(), ...(mode === 'web' ? [viteSingleFile()] : [])],
  clearScreen: false,
  server: { port: 1420, strictPort: true },
  build: {
    outDir: mode === 'web' ? 'dist-web' : 'dist',
    target: 'es2021',
    chunkSizeWarningLimit: 4000,
  },
}));
