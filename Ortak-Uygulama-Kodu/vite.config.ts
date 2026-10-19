import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// API isteklerini geliştirmede ASP.NET Core sunucusuna yönlendirir.
export default defineConfig({
  plugins: [react()],
  clearScreen: false,
  server: { port: 5173, strictPort: true, proxy: { '/api': 'http://localhost:5080' } },
});
