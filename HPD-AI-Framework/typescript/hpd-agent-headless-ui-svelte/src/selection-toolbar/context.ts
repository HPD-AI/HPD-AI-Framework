import { createContext } from 'svelte';
import type { SelectionToolbarRootContext } from './types.js';

export const [getSelectionToolbarContext, setSelectionToolbarContext] =
  createContext<SelectionToolbarRootContext>();
