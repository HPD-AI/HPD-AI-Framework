import type { HpdosRuntimeApi } from "./hpdosState.js";
import type { HpdosRuntime } from "./hpdosWorkspace.js";

export class FetchHpdosRuntimeApi implements HpdosRuntimeApi {
  constructor(private readonly runtimeUrl = "/api/hpdos/runtime") {
  }

  async loadRuntime() {
    const response = await fetch(this.runtimeUrl, { credentials: "include" });
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to load HPDOS runtime: HTTP ${response.status}: ${text}`);
    }

    return await response.json() as HpdosRuntime;
  }
}
