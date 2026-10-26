/// <reference types="vitest/config" />
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';
import { VitePWA } from 'vite-plugin-pwa';
import { fileURLToPath, URL } from 'node:url';

export default defineConfig({
  plugins: [
    react(),
    tailwindcss(),
    VitePWA({
      // The app asks before activating a new version so a sale in progress is never reloaded.
      registerType: 'prompt',
      injectRegister: null,
      includeAssets: ['favicon.svg', 'icons/apple-touch-icon.png'],
      manifest: {
        id: '/',
        name: 'معرض قويدر للأدوات والأجهزة المنزلية',
        short_name: 'معرض قويدر',
        description: 'إدارة المبيعات والمخزون والمشتريات والحسابات لمعرض قويدر',
        lang: 'ar',
        dir: 'rtl',
        start_url: '/',
        scope: '/',
        display: 'standalone',
        orientation: 'any',
        theme_color: '#0f5f5a',
        background_color: '#f4f6f6',
        categories: ['business', 'finance', 'productivity'],
        icons: [
          { src: '/icons/icon-192.png', sizes: '192x192', type: 'image/png', purpose: 'any' },
          { src: '/icons/icon-512.png', sizes: '512x512', type: 'image/png', purpose: 'any' },
          { src: '/icons/maskable-192.png', sizes: '192x192', type: 'image/png', purpose: 'maskable' },
          { src: '/icons/maskable-512.png', sizes: '512x512', type: 'image/png', purpose: 'maskable' },
        ],
        // Long-press (phone) / right-click (desktop) shortcuts on the installed icon.
        shortcuts: [
          { name: 'بيع جديد', url: '/pos', icons: [{ src: '/icons/icon-192.png', sizes: '192x192' }] },
          { name: 'إضافة منتج', url: '/products?add=1', icons: [{ src: '/icons/icon-192.png', sizes: '192x192' }] },
          { name: 'البحث عن منتج', url: '/products?focus=1', icons: [{ src: '/icons/icon-192.png', sizes: '192x192' }] },
          { name: 'المخزون', url: '/inventory', icons: [{ src: '/icons/icon-192.png', sizes: '192x192' }] },
        ],
      },
      workbox: {
        // App shell only. API responses (stock, prices, balances) are never cached: stale numbers are worse than none.
        globPatterns: ['**/*.{js,css,html,woff2,svg,png}'],
        navigateFallback: '/index.html',
        navigateFallbackDenylist: [/^\/api\//, /^\/swagger/],
        // Phone notifications (push + tap to open) live in their own small file.
        importScripts: ['push-sw.js'],
        cleanupOutdatedCaches: true,
        runtimeCaching: [
          {
            // Product photos are public and immutable (served with Cache-Control: public, immutable).
            // Private files (transfer proofs) are served no-store and are never written to the cache.
            urlPattern: ({ url }) => url.pathname.startsWith('/api/files/'),
            handler: 'CacheFirst',
            options: {
              cacheName: 'product-images',
              expiration: { maxEntries: 600, maxAgeSeconds: 60 * 60 * 24 * 90 },
              plugins: [
                {
                  cacheWillUpdate: async ({ response }) =>
                    response.ok && (response.headers.get('Cache-Control') ?? '').includes('public') ? response : null,
                },
              ],
            },
          },
        ],
      },
      devOptions: { enabled: false },
    }),
  ],
  resolve: {
    alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) },
  },
  server: {
    host: true, // reachable from a phone on the same network
    port: 5173,
    proxy: { '/api': { target: 'http://localhost:5298', changeOrigin: false } },
  },
  build: {
    // The API serves the built app from wwwroot in production.
    outDir: '../src/Qwader.Api/wwwroot',
    emptyOutDir: true,
    rollupOptions: {
      output: {
        // Libraries change rarely; keeping them in their own files lets installed apps reuse them across updates.
        manualChunks: {
          react: ['react', 'react-dom', 'react-router'],
          data: ['@tanstack/react-query', 'react-hook-form', '@hookform/resolvers', 'zod'],
          icons: ['lucide-react'],
        },
      },
    },
  },
  test: {
    environment: 'node',
  },
});
