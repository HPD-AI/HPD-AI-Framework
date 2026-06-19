import type { Message } from '../thread/types.js';

export interface MessageDirective {
  id: string;
  label: string;
  text: string;
  trigger: string;
  type: string;
  metadata?: Record<string, unknown>;
}

export type DirectiveTextPart =
  | DirectiveTextPlainPart
  | DirectiveTextDirectivePart;

export interface DirectiveTextPlainPart {
  id: string;
  text: string;
  type: 'text';
}

export interface DirectiveTextDirectivePart {
  directive: MessageDirective;
  id: string;
  text: string;
  type: 'directive';
}

export interface CreateMessageDirectiveOptions {
  id: string;
  label: string;
  metadata?: Record<string, unknown>;
  text?: string;
  trigger: string;
  type: string;
}

export interface CreateDirectiveTextPartsOptions {
  directives?: readonly MessageDirective[];
  message?: Pick<Message, 'additionalProperties'> | null;
  text: string;
}
