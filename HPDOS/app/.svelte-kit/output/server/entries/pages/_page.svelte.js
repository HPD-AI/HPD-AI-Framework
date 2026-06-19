import { a4 as head, a5 as ensure_array_like, a6 as attr_class, e as escape_html, a7 as attr, a3 as derived } from "../../chunks/index.js";
function _page($$renderer, $$props) {
  $$renderer.component(($$renderer2) => {
    let sessions = [];
    let threads = [];
    let activeSessionId = "";
    let activeThreadId = "main";
    let statusMessage = "Starting Branch Lab.";
    const branchControls = derived(() => {
      return [];
    });
    function threadLabel(item) {
      return item.name || item.id;
    }
    function sessionLabel(item) {
      return String(item.metadata?.name ?? item.id);
    }
    head("1uha8ag", $$renderer2, ($$renderer3) => {
      $$renderer3.title(($$renderer4) => {
        $$renderer4.push(`<title>HPD-OS Branch Lab</title>`);
      });
    });
    $$renderer2.push(`<div class="shell svelte-1uha8ag"><aside class="sidebar svelte-1uha8ag"><div class="brand svelte-1uha8ag"><span class="svelte-1uha8ag">HPD-OS</span> <h1 class="svelte-1uha8ag">Branch Lab</h1> <p class="svelte-1uha8ag">Current headless thread primitives, no archive branch actions.</p></div> <div class="stack svelte-1uha8ag"><button type="button" class="wide svelte-1uha8ag">New session</button> <button type="button" class="wide svelte-1uha8ag">Refresh</button></div> <section class="panel svelte-1uha8ag"><h2 class="svelte-1uha8ag">Sessions</h2> `);
    if (sessions.length === 0) {
      $$renderer2.push("<!--[0-->");
      $$renderer2.push(`<p class="muted svelte-1uha8ag">No sessions yet.</p>`);
    } else {
      $$renderer2.push("<!--[-1-->");
      $$renderer2.push(`<div class="list svelte-1uha8ag"><!--[-->`);
      const each_array = ensure_array_like(sessions);
      for (let $$index = 0, $$length = each_array.length; $$index < $$length; $$index++) {
        let session = each_array[$$index];
        $$renderer2.push(`<button type="button"${attr_class("svelte-1uha8ag", void 0, { "active": session.id === activeSessionId })}><strong>${escape_html(sessionLabel(session))}</strong> <small class="svelte-1uha8ag">${escape_html(session.id)}</small></button>`);
      }
      $$renderer2.push(`<!--]--></div>`);
    }
    $$renderer2.push(`<!--]--></section> <section class="panel svelte-1uha8ag"><h2 class="svelte-1uha8ag">Threads</h2> `);
    if (threads.length === 0) {
      $$renderer2.push("<!--[0-->");
      $$renderer2.push(`<p class="muted svelte-1uha8ag">No threads loaded.</p>`);
    } else {
      $$renderer2.push("<!--[-1-->");
      $$renderer2.push(`<div class="list svelte-1uha8ag"><!--[-->`);
      const each_array_1 = ensure_array_like(threads);
      for (let $$index_1 = 0, $$length = each_array_1.length; $$index_1 < $$length; $$index_1++) {
        let item = each_array_1[$$index_1];
        $$renderer2.push(`<button type="button"${attr_class("svelte-1uha8ag", void 0, { "active": item.id === activeThreadId })}><strong>${escape_html(threadLabel(item))}</strong> <small class="svelte-1uha8ag">${escape_html(item.id)}</small></button>`);
      }
      $$renderer2.push(`<!--]--></div>`);
    }
    $$renderer2.push(`<!--]--></section></aside> <main class="main svelte-1uha8ag"><header class="topbar svelte-1uha8ag"><div><h2 class="svelte-1uha8ag">${escape_html(activeThreadId)}</h2> <p class="svelte-1uha8ag">${escape_html("No session")}</p></div> `);
    {
      $$renderer2.push("<!--[-1-->");
    }
    $$renderer2.push(`<!--]--></header> <div class="notice svelte-1uha8ag"${attr("data-error", void 0)}>${escape_html(statusMessage)}</div> <section class="timeline svelte-1uha8ag">`);
    {
      $$renderer2.push("<!--[0-->");
      $$renderer2.push(`<div class="empty svelte-1uha8ag">Loading thread.</div>`);
    }
    $$renderer2.push(`<!--]--></section> `);
    {
      $$renderer2.push("<!--[-1-->");
    }
    $$renderer2.push(`<!--]--></main> <aside class="inspector svelte-1uha8ag"><section class="panel svelte-1uha8ag"><h2 class="svelte-1uha8ag">Fork Groups</h2> `);
    {
      $$renderer2.push("<!--[0-->");
      $$renderer2.push(`<p class="muted svelte-1uha8ag">No forks yet.</p>`);
    }
    $$renderer2.push(`<!--]--></section> <section class="panel snapshot svelte-1uha8ag"><h2 class="svelte-1uha8ag">Snapshot</h2> <dl class="svelte-1uha8ag"><div class="svelte-1uha8ag"><dt class="svelte-1uha8ag">Messages</dt><dd class="svelte-1uha8ag">${escape_html(0)}</dd></div> <div class="svelte-1uha8ag"><dt class="svelte-1uha8ag">Timeline</dt><dd class="svelte-1uha8ag">${escape_html(0)}</dd></div> <div class="svelte-1uha8ag"><dt class="svelte-1uha8ag">Fork groups</dt><dd class="svelte-1uha8ag">${escape_html(0)}</dd></div> <div class="svelte-1uha8ag"><dt class="svelte-1uha8ag">Controls</dt><dd class="svelte-1uha8ag">${escape_html(branchControls().length)}</dd></div></dl></section></aside></div>`);
  });
}
export {
  _page as default
};
