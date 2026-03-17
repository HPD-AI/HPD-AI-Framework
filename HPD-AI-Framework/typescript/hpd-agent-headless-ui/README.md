# HPD Agent Headless UI

**The world's first headless component library specifically for AI chat interfaces.**

## Our Unique Position

**HPD Headless UI** is the first library to combine truly headless architecture with AI-specific primitives.

**What this means:**
-   **Truly Headless** - Zero CSS, you control all styling
-   **AI-Specific Primitives** - Streaming, tools, permissions built-in
-   **Protocol-First Design** - Maps directly to HPD events

---

## Features

- 🎯 **Simple API** - 8 lines of code for a working AI chat with streaming
- 🚀 **Protocol-first** - Maps directly to HPD's 67 events
- 💎 **Truly headless** - Zero CSS shipped, data attributes only
- ⚡ **Fine-grained reactivity** - Automatic updates with runes
- 📦 **Bundle conscious** - < 20 KB total
- 🎨 **Complete styling control** - Build iMessage, ChatGPT, or your own design

---

## Quick Start

```bash
npm install @hpd/hpd-agent-headless-ui
```

```svelte
<script>
import { createMockAgent } from '@hpd/hpd-agent-headless-ui';

const agent = createMockAgent();
let input = '';

async function send() {
  await agent.send(input);
  input = '';
}
</script>

<div class="chat">
  {#each agent.state.messages as message}
    <div class="message {message.role}">
      {message.content}
      {#if message.streaming}
        <span class="cursor">▊</span>
      {/if}
    </div>
  {/each}

  <input bind:value={input} />
  <button onclick={send}>Send</button>
</div>

<style>
  /* You provide ALL the styling - we provide the reactivity */
  .chat { /* your styles */ }
  .message { /* your styles */ }
  .cursor { /* your styles */ }
</style>
```

**That's it!** 8 lines of code for a working AI chat with streaming text.

---

## Architecture

```
HPD Backend → AgentClient → EventMapper → AgentState → Svelte Reactivity → Your UI
              (15 KB)       (NEW!)        ($state)    (automatic)       (your design)
```

**Protocol-First Design:**
- HPD's events map directly to AgentState updates
- Svelte runes make updates automatic (no manual subscriptions)
- You just read `agent.state.messages` - it's always current

---

## Documentation

- **Full Proposal:** [InternalDocs/Proposal/README.md](../../InternalDocs/ShellOS/Proposal/hpd-agent-headless-ui/README.md)
- **Developer Guide:** [CLAUDE.md](./CLAUDE.md)
- **API Reference:** Coming soon

---

## Development

```bash
# Development
npm run dev                # Start dev server
npm run check              # Type checking

# Testing
npm run test:unit          # Unit tests (13 passing)
npm run test               # All tests

# Analysis
npm run analyze            # Bundle size analysis

# Building
npm run build              # Build library
npm run prepack            # Package for NPM
```

---

## License

MIT
