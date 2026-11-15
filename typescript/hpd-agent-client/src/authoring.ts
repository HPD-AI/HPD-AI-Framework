import type { ChatClientConfig } from './generated/models.js';

/**
 * Creates a generated Chat client configuration fragment for one provider.
 */
export function model(
  provider: string,
  modelName: string,
  options: Omit<ChatClientConfig, 'provider' | 'modelName'> = {},
): ChatClientConfig {
  const normalizedProvider = provider.trim();
  const normalizedModel = modelName.trim();
  if (normalizedProvider.length === 0)
    throw new TypeError('provider must be a non-empty string');
  if (normalizedModel.length === 0)
    throw new TypeError('modelName must be a non-empty string');

  return {
    ...options,
    provider: { key: normalizedProvider },
    modelName: normalizedModel,
  };
}
