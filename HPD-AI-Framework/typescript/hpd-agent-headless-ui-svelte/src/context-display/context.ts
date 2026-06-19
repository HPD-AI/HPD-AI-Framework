import { getContext, setContext } from 'svelte';
import type {
  ContextDisplayModel,
} from './types.js';

const contextDisplayKey = Symbol('hpd-context-display');

export interface ContextDisplayContext {
  getModel(): ContextDisplayModel;
}

export function setContextDisplayContext(context: ContextDisplayContext): void {
  setContext(contextDisplayKey, context);
}

export function getContextDisplayContext(): ContextDisplayContext {
  const context = getContext<ContextDisplayContext | undefined>(contextDisplayKey);
  if (!context) {
    throw new Error('ContextDisplay primitives must be used inside ContextDisplayRoot.');
  }
  return context;
}

