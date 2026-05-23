import type { ElectrobunConfig } from "electrobun";
import { existsSync } from "node:fs";

const copy: NonNullable<ElectrobunConfig["build"]>["copy"] = {
  "src/mainview/loading.html": "views/mainview/loading.html"
};

if (existsSync("resources/backend")) {
  copy["resources/backend"] = "backend";
}

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
    copy,
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
