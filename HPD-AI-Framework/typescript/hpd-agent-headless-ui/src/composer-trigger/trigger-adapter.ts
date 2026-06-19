import type {
  ComposerTriggerAdapter,
  ComposerTriggerCategory,
  ComposerTriggerItem,
} from './types.js';

export function getComposerTriggerCategories(
  adapter: ComposerTriggerAdapter | undefined,
): ComposerTriggerCategory[] {
  return [...(adapter?.categories?.() ?? [])];
}

export function getComposerTriggerItems(
  adapter: ComposerTriggerAdapter | undefined,
  query = '',
  categoryId?: string | null,
): ComposerTriggerItem[] {
  if (!adapter) return [];

  if (query && adapter.search) {
    return [...adapter.search(query)];
  }

  if (categoryId && adapter.categoryItems) {
    return [...adapter.categoryItems(categoryId)];
  }

  if (adapter.items) {
    return [...adapter.items()];
  }

  const categories = adapter.categories?.() ?? [];
  if (categories.length > 0 && adapter.categoryItems) {
    return categories.flatMap((category) => [...adapter.categoryItems?.(category.id) ?? []]);
  }

  return [];
}

export function createStaticComposerTriggerAdapter(options: {
  categories?: readonly ComposerTriggerCategory[];
  items: readonly ComposerTriggerItem[];
}): ComposerTriggerAdapter {
  const categories = [...(options.categories ?? [])];
  const items = [...options.items];

  return {
    categories: categories.length > 0 ? () => categories : undefined,
    categoryItems: categories.length > 0
      ? (categoryId) => items.filter((item) => item.categoryId === categoryId)
      : undefined,
    items: () => items,
    search: (query) => {
      const normalized = query.trim().toLocaleLowerCase();
      if (!normalized) return items;
      return items.filter((item) =>
        item.id.toLocaleLowerCase().includes(normalized) ||
        item.label.toLocaleLowerCase().includes(normalized) ||
        item.description?.toLocaleLowerCase().includes(normalized));
    },
  };
}
