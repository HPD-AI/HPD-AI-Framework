import type { AgentRunInputEvent } from '@hpd-research/hpd-agent-client';
import type { RuntimeRequest } from '@hpd-research/hpd-agent-headless-ui';
import type { ThreadState } from '../thread-state.js';
import { mergeProps } from '../thread-composer/index.js';
import type {
  RuntimeRequestActions,
  RuntimeRequestActionProps,
  RuntimeRequestElementProps,
  RuntimeRequestKindElementProps,
} from './types.js';

export interface CreateRuntimeRequestElementPropsOptions {
  item: RuntimeRequest;
  restProps?: Record<string, unknown>;
}

export function createRuntimeRequestElementProps(
  options: CreateRuntimeRequestElementPropsOptions,
): RuntimeRequestElementProps {
  const { item, restProps = {} } = options;
  return mergeProps(restProps, {
    'aria-label': `${item.kind} runtime request from ${item.sourceName}`,
    'data-hpd-runtime-request': '',
    'data-request-id': item.id,
    'data-request-kind': item.kind,
    'data-request-source': item.sourceName,
    'data-request-event-type': item.requestEventType,
    'data-response-event-type': item.expectedResponseEventType,
    'data-response-policy': item.responsePolicy,
    'data-visibility': item.visibility,
  }) as unknown as RuntimeRequestElementProps;
}

export function createRuntimeRequestKindElementProps(options: {
  item: RuntimeRequest;
  restProps?: Record<string, unknown>;
}): RuntimeRequestKindElementProps {
  return mergeProps(options.restProps ?? {}, {
    'data-hpd-runtime-request-kind': options.item.kind,
  }) as unknown as RuntimeRequestKindElementProps;
}

export function createRuntimeRequestActionProps(options: {
  canApprove: boolean;
  canDeny: boolean;
  canSubmit: boolean;
  onApproveClick: (event: MouseEvent) => void;
  onDenyClick: (event: MouseEvent) => void;
}): RuntimeRequestActionProps {
  return {
    approve: {
      'aria-disabled': !options.canApprove,
      'data-hpd-runtime-request-approve': '',
      disabled: !options.canApprove,
      onclick: options.onApproveClick,
      type: 'button',
    },
    deny: {
      'aria-disabled': !options.canDeny,
      'data-hpd-runtime-request-deny': '',
      disabled: !options.canDeny,
      onclick: options.onDenyClick,
      type: 'button',
    },
    submit: {
      'aria-disabled': !options.canSubmit,
      'data-hpd-runtime-request-submit': '',
      disabled: !options.canSubmit,
      type: 'submit',
    },
  };
}

export function createRuntimeRequestActions(
  item: RuntimeRequest,
  thread?: ThreadState,
): RuntimeRequestActions {
  return {
    approve(choice) {
      return item.kind === 'permission' && thread
        ? thread.approve(item.id, choice)
        : Promise.resolve(undefined);
    },
    clarify(answer) {
      return item.kind === 'clarification' && thread
        ? thread.clarify(item.id, answer)
        : Promise.resolve(undefined);
    },
    deny(reason) {
      return item.kind === 'permission' && thread
        ? thread.deny(item.id, reason)
        : Promise.resolve(undefined);
    },
    respond(input) {
      return thread ? thread.respond(input) : Promise.resolve(undefined);
    },
    respondToClientTool(response, options) {
      return item.kind === 'client-tool' && thread
        ? thread.respondToClientTool(item.id, response, options)
        : Promise.resolve(undefined);
    },
  };
}

export function createCustomResponseInput(
  item: RuntimeRequest,
  value: string,
): AgentRunInputEvent {
  return {
    type: item.expectedResponseEventType ?? `${item.requestEventType}_RESPONSE`,
    requestId: item.id,
    sourceName: item.sourceName,
    value,
  } as unknown as AgentRunInputEvent;
}
