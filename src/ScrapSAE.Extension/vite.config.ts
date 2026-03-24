import { defineConfig } from 'vite';
import { resolve } from 'path';

export default defineConfig({
  build: {
    outDir: 'dist',
    emptyOutDir: true,
    rollupOptions: {
      input: {
        service_worker: resolve(__dirname, 'src/background/service_worker.ts'),
        extractor: resolve(__dirname, 'src/content/extractor.ts'),
        human_simulator: resolve(__dirname, 'src/content/human_simulator.ts'),
        popup: resolve(__dirname, 'src/popup/popup.html'),
        sidepanel: resolve(__dirname, 'src/sidepanel/sidepanel.html'),
      },
      output: {
        entryFileNames: (chunkInfo) => {
          const nameMap: Record<string, string> = {
            service_worker: 'src/background/service_worker.js',
            extractor: 'src/content/extractor.js',
            human_simulator: 'src/content/human_simulator.js',
          };
          return nameMap[chunkInfo.name] || 'assets/[name]-[hash].js';
        },
        chunkFileNames: 'assets/[name]-[hash].js',
        assetFileNames: 'assets/[name]-[hash].[ext]',
      },
    },
    target: 'esnext',
    minify: false, // Firefox AMO requires readable code
  },
  resolve: {
    alias: {
      '@shared': resolve(__dirname, 'src/shared'),
    },
  },
  publicDir: 'public',
});
