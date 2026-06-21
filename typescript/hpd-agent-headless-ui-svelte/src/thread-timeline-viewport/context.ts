import { createContext } from 'svelte';
import type { ThreadTimelineViewportApi } from './types.js';

export const [
  getThreadTimelineViewportContext,
  setThreadTimelineViewportContext,
] = createContext<ThreadTimelineViewportApi>();
