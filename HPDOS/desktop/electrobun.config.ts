import type { ElectrobunConfig } from "electrobun";

export default {
  app: {
    name: "HPD-OS",
    identifier: "dev.hpdos.desktop",
    version: "0.0.1"
  },
  runtime: {
    exitOnLastWindowClosed: true
  },
  build: {
    bun: {
      entrypoint: "src/bun/index.ts"
    },
    copy: {
      "src/mainview/loading.html": "views/mainview/loading.html",
      "resources/backend": "backend"
    },
    mac: {
      bundleCEF: false
    },
    linux: {
      bundleCEF: false
    },
    win: {
      bundleCEF: false
    }
  }
} satisfies ElectrobunConfig;
