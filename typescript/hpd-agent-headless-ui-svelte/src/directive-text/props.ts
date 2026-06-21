import type {
  DirectiveTextDirectivePart,
  DirectiveTextPart,
  DirectiveTextPlainPart,
} from '@hpd-research/hpd-agent-headless-ui';
import { mergeProps } from '../thread-composer/index.js';
import type {
  DirectiveTextChipElementProps,
  DirectiveTextPlainElementProps,
  DirectiveTextRootElementProps,
} from './types.js';

export function createDirectiveTextRootElementProps(
  restProps: Record<string, unknown> = {},
): DirectiveTextRootElementProps {
  return mergeProps(restProps, {
    'data-hpd-directive-text': '',
  }) as unknown as DirectiveTextRootElementProps;
}

export function createDirectiveTextPartElementProps(
  part: DirectiveTextPart,
  restProps: Record<string, unknown> = {},
): DirectiveTextPlainElementProps | DirectiveTextChipElementProps {
  if (part.type === 'directive') {
    return createDirectiveTextChipElementProps(part, restProps);
  }

  return createDirectiveTextPlainElementProps(part, restProps);
}

export function createDirectiveTextPlainElementProps(
  _part: DirectiveTextPlainPart,
  restProps: Record<string, unknown> = {},
): DirectiveTextPlainElementProps {
  return mergeProps(restProps, {
    'data-hpd-directive-text-part': '',
    'data-part-type': 'text',
  }) as unknown as DirectiveTextPlainElementProps;
}

export function createDirectiveTextChipElementProps(
  part: DirectiveTextDirectivePart,
  restProps: Record<string, unknown> = {},
): DirectiveTextChipElementProps {
  return mergeProps(restProps, {
    'aria-label': `${part.directive.trigger}${part.directive.label}`,
    'data-directive-id': part.directive.id,
    'data-directive-trigger': part.directive.trigger,
    'data-directive-type': part.directive.type,
    'data-hpd-directive-text-chip': '',
    'data-hpd-directive-text-part': '',
    'data-part-type': 'directive',
  }) as unknown as DirectiveTextChipElementProps;
}
