import { createContext } from 'svelte';
import type { StudioShellServices } from './contracts.ts';

export const [useStudioShell, provideStudioShell] = createContext<StudioShellServices>();
