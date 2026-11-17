import { svelte } from "@sveltejs/vite-plugin-svelte";
import { fileURLToPath } from "node:url";
import { defineConfig } from "vite";

export default defineConfig({
  plugins: [svelte()],
  resolve: {
    alias: {
      "@hpd/base-client": fileURLToPath(new URL("../../../hpd-base-client/src/index.ts", import.meta.url)),
      "@noble/hashes": fileURLToPath(new URL("./node_modules/@noble/hashes", import.meta.url))
    }
  },
  build: {
    outDir: "dist",
    emptyOutDir: true,
    lib: { entry: "src/index.ts", formats: ["es"], fileName: () => "agent.js" },
    cssCodeSplit: false
  }
});
