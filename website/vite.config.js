import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';

// base = /setup-toolbox/ omdat de site op projects.dpvb.nl/setup-toolbox draait
// (subpad), zodat asset-URL's relatief daaraan kloppen.
export default defineConfig({
  base: '/setup-toolbox/',
  plugins: [react(), tailwindcss()],
});
