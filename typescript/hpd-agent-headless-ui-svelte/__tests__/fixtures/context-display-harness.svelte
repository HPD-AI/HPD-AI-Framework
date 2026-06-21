<script lang="ts">
  import {
    ContextDisplayBar,
    ContextDisplayBreakdown,
    ContextDisplayRoot,
    ContextDisplayText,
  } from '../../src/context-display/index.js';

  const usage = {
    inputTokenCount: 700,
    outputTokenCount: 200,
    totalTokenCount: 1000,
    cachedInputTokenCount: 100,
    reasoningTokenCount: 50,
  };
</script>

<ContextDisplayRoot {usage} modelContextWindow={2000}>
  <ContextDisplayBar />
  <ContextDisplayText />
  <ContextDisplayBreakdown />
</ContextDisplayRoot>

