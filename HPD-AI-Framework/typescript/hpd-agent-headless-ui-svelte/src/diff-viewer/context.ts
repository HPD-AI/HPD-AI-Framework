import { createContext } from 'svelte';
import type { DiffViewerContext } from './types.js';

export const [getDiffViewerContext, setDiffViewerContext] = createContext<DiffViewerContext>();
