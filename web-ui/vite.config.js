/// <reference types="vitest" />
import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';
// https://vitejs.dev/config/
export default defineConfig({
    plugins: [react()],
    server: {
        proxy: {
            '/api': {
                target: 'http://127.0.0.1:5108', // Backend URL
                changeOrigin: true,
            }
        }
    },
    test: {
        environment: 'jsdom',
        setupFiles: ['./src/vitest.setup.ts'],
        globals: true,
    }
});
