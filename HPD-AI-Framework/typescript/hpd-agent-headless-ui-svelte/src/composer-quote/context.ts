import { createContext } from 'svelte';
import type { ComposerQuoteContext } from './types.js';

export const [getComposerQuoteContext, setComposerQuoteContext] =
  createContext<ComposerQuoteContext>();
