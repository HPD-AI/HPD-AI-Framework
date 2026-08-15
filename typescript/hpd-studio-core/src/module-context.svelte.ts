import { createContext } from 'svelte';
import type { StudioModuleContextReader } from './contracts.ts';

export const [useStudioModuleContext, provideStudioModuleContext] =
  createContext<StudioModuleContextReader>();
