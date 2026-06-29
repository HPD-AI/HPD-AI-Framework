import { createContext } from 'svelte';
import type { StudioController } from '../types';

export const [getStudioContext, setStudioContext] = createContext<() => StudioController>();
