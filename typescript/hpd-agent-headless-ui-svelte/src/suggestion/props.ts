import { createTextContent } from '@hpd-research/hpd-agent-client';
import { mergeProps } from '../thread-composer/index.js';
import type {
  SuggestionActions,
  SuggestionBlockedReason,
  SuggestionElementProps,
  SuggestionListElementProps,
  SuggestionMode,
  SuggestionModel,
  SuggestionPopulateMode,
  SuggestionProps,
  SuggestionSelectDetails,
} from './types.js';

export interface CreateSuggestionModelOptions {
  additionalProperties?: Record<string, unknown>;
  description?: string;
  disabled?: boolean;
  mode?: SuggestionMode;
  persistSuggestionMetadata?: boolean;
  populateMode?: SuggestionPopulateMode;
  prompt: string;
  submitting?: boolean;
  thread?: SuggestionProps['thread'];
  title?: string;
}

export interface CreateSuggestionElementPropsOptions {
  model: SuggestionModel;
  onclick: (event: MouseEvent) => void;
  restProps?: Record<string, unknown>;
}

export interface CreateSuggestionActionsOptions {
  model: SuggestionModel;
  onSelect?: SuggestionProps['onSelect'];
  runConfig?: SuggestionProps['runConfig'];
  getTargetValue?: () => string;
  setTargetValue?: (value: string) => void;
  setSubmitting?: (value: boolean) => void;
}

export function createSuggestionModel(options: CreateSuggestionModelOptions): SuggestionModel {
  const mode = options.mode ?? 'populate';
  const populateMode = options.populateMode ?? 'replace';
  const thread = options.thread ?? null;
  const prompt = options.prompt;
  const title = options.title ?? prompt;
  const description = options.description ?? '';
  const submitting = options.submitting ?? false;
  const blockedReason = getSuggestionBlockedReason({
    disabled: options.disabled ?? false,
    mode,
    prompt,
    submitting,
    thread,
  });

  return {
    additionalProperties: options.additionalProperties,
    blockedReason,
    canSelect: blockedReason === null,
    description,
    mode,
    persistSuggestionMetadata: options.persistSuggestionMetadata ?? true,
    populateMode,
    prompt,
    submitting,
    thread,
    title,
  };
}

export function createSuggestionElementProps(
  options: CreateSuggestionElementPropsOptions,
): SuggestionElementProps {
  const { model, onclick, restProps = {} } = options;

  return mergeProps(restProps, {
    'aria-disabled': !model.canSelect,
    'data-blocked-reason': model.blockedReason ?? undefined,
    'data-can-select': model.canSelect ? '' : undefined,
    'data-hpd-suggestion': '',
    'data-mode': model.mode,
    'data-populate-mode': model.populateMode,
    'data-submitting': model.submitting ? '' : undefined,
    disabled: !model.canSelect,
    onclick,
    type: 'button',
  }) as unknown as SuggestionElementProps;
}

export function createSuggestionListElementProps(
  restProps: Record<string, unknown> = {},
): SuggestionListElementProps {
  return mergeProps(restProps, {
    'data-hpd-suggestion-list': '',
  }) as unknown as SuggestionListElementProps;
}

export function createSuggestionActions(options: CreateSuggestionActionsOptions): SuggestionActions {
  return {
    async select() {
      const { model } = options;
      if (!model.canSelect) return;

      const details: SuggestionSelectDetails = {
        additionalProperties: model.additionalProperties,
        description: model.description,
        mode: model.mode,
        populateMode: model.populateMode,
        prompt: model.prompt,
        thread: model.thread,
        title: model.title,
      };

      if (model.mode === 'populate') {
        const currentValue = options.getTargetValue?.() ?? '';
        const nextValue = model.populateMode === 'append'
          ? appendPrompt(currentValue, model.prompt)
          : model.prompt;
        options.setTargetValue?.(nextValue);
        await options.onSelect?.(details);
        return;
      }

      if (!model.thread) return;

      options.setSubmitting?.(true);
      try {
        await model.thread.sendMessage({
          contents: [createTextContent(model.prompt)],
          additionalProperties: createSuggestionAdditionalProperties(model),
        }, { runConfig: options.runConfig });
        await options.onSelect?.(details);
      } finally {
        options.setSubmitting?.(false);
      }
    },
  };
}

function getSuggestionBlockedReason(options: {
  disabled: boolean;
  mode: SuggestionMode;
  prompt: string;
  submitting: boolean;
  thread: SuggestionProps['thread'] | null;
}): SuggestionBlockedReason {
  if (options.disabled) return 'disabled';
  if (!options.prompt.trim()) return 'empty';
  if (options.submitting) return 'submitting';
  if (options.mode === 'populate') return null;
  if (!options.thread) return 'missing-thread';

  const snapshot = options.thread.getSnapshot();
  if (!snapshot.canSubmitText) {
    return snapshot.textSubmissionState.reason ?? 'not-sendable';
  }

  return null;
}

function appendPrompt(currentValue: string, prompt: string): string {
  return currentValue.trim()
    ? `${currentValue.trimEnd()} ${prompt}`
    : prompt;
}

function createSuggestionAdditionalProperties(
  model: SuggestionModel,
): Record<string, unknown> | undefined {
  const suggestionMetadata = model.persistSuggestionMetadata
    ? {
        prompt: model.prompt,
        title: model.title,
        description: model.description,
      }
    : undefined;

  if (!model.additionalProperties && !suggestionMetadata) return undefined;

  return {
    ...(model.additionalProperties ?? {}),
    ...(suggestionMetadata ? { suggestion: suggestionMetadata } : {}),
  };
}
