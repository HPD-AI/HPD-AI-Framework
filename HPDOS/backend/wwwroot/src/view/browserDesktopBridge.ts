import Electrobun, { Electroview, type RPCSchema } from "../../../../desktop/node_modules/electrobun/dist/api/browser/index.ts";
import type { HpdosDesktopBridge } from "../core/hpdosState.js";

type HpdosDesktopRPC = {
  bun: RPCSchema<{
    requests: {
      pickWorkspaceFolders: {
        params: {};
        response: string[];
      };
    };
    messages: {};
  }>;
  webview: RPCSchema<{
    requests: {};
    messages: {};
  }>;
};

declare global {
  interface Window {
    hpdosDesktop?: HpdosDesktopBridge;
  }
}

export class BrowserDesktopBridge implements HpdosDesktopBridge {
  private bridge: HpdosDesktopBridge | null = null;

  async pickWorkspaceFolders() {
    const bridge = this.ensureBridge();
    if (!bridge?.pickWorkspaceFolders) throw new Error("Folder picker is not connected.");
    return bridge.pickWorkspaceFolders();
  }

  private ensureBridge() {
    if (this.bridge) return this.bridge;
    if (window.hpdosDesktop?.pickWorkspaceFolders) {
      this.bridge = window.hpdosDesktop;
      return this.bridge;
    }
    if (!window.__electrobun) return null;

    const rpc = Electroview.defineRPC<HpdosDesktopRPC>({
      maxRequestTime: 60_000,
      handlers: {
        requests: {},
        messages: {}
      }
    });
    const electrobun = new Electrobun.Electroview({ rpc });
    this.bridge = {
      pickWorkspaceFolders: () => electrobun.rpc!.request.pickWorkspaceFolders({})
    };
    window.hpdosDesktop = this.bridge;
    return this.bridge;
  }
}
