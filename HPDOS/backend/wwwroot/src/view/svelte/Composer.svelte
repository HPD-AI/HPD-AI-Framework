<script lang="ts">
  import type { HpdosState } from "../../core/hpdosState.js";
  import type { ViewActions } from "./types.js";

  let { appState, actions, draft }: { appState: HpdosState; actions: ViewActions; draft: string } = $props();

  function submit(event: SubmitEvent) {
    event.preventDefault();
    const form = event.currentTarget as HTMLFormElement;
    const text = String(new FormData(form).get("text") || "").trim();
    if (!text) return;
    actions.setDraft("");
    actions.sendText({
      text,
      providerKey: appState.providerKey,
      modelId: appState.modelId
    });
  }
</script>

<form class="hpd-composer" id="composer" onsubmit={submit}>
  <div class="hpd-composer-box">
    <textarea
      class="hpd-composer-input"
      id="text"
      name="text"
      value={draft}
      oninput={(event) => actions.setDraft(event.currentTarget.value)}
      autocomplete="off"
      autocapitalize="sentences"
      enterkeyhint="send"
      placeholder="Ask HPD-OS to inspect, edit, run, or explain..."
      required
      spellcheck="true"></textarea>
    <div class="hpd-composer-footer">
      <button class="hpd-runtime-trigger" popovertarget="runtime-popover" type="button">Runtime</button>
      <div class="hpd-runtime-popover" id="runtime-popover" popover>
        <input class="hpd-input" autocomplete="off" spellcheck="false" value={appState.providerKey} oninput={(event) => actions.setRuntimeOptions({ providerKey: event.currentTarget.value })} />
        <input class="hpd-input" autocomplete="off" spellcheck="false" value={appState.modelId} oninput={(event) => actions.setRuntimeOptions({ modelId: event.currentTarget.value })} />
      </div>
      <button class="hpd-button hpd-button-primary" disabled={appState.busy} id="send" type="submit">Send -&gt;</button>
    </div>
  </div>
</form>
