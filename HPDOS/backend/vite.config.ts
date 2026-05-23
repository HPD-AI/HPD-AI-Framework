import { svelte } from "@sveltejs/vite-plugin-svelte";
import tailwindcss from "@tailwindcss/vite";
import { defineConfig } from "vite";

export default defineConfig({
  base: "/assets/",
  plugins: [tailwindcss(), svelte()],
  build: {
    emptyOutDir: false,
    outDir: "wwwroot/assets",
    rollupOptions: {
      input: "wwwroot/src/view/main.ts",
      output: {
        entryFileNames: "app.js",
        chunkFileNames: "[name].js",
        assetFileNames: (assetInfo) => assetInfo.names.some((name) => name.endsWith(".css")) ? "app.css" : "[name][extname]"
      }
    }
  }
});
