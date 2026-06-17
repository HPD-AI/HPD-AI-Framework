/**
 * Arbitrary Generators for Property-Based Testing
 *
 * Generates random but valid operations for testing with fast-check.
 * Based on ShellOSUI V3 Final Architecture proposal section 8.1.3
 */

import * as fc from 'fast-check';

/**
 * Generate arbitrary panel IDs.
 * Uses alphanumeric characters to create realistic panel IDs.
 */
export const arbPanelId = fc.array(
	fc.constantFrom(...'abcdefghijklmnopqrstuvwxyz0123456789'.split('')),
	{
		minLength: 4,
		maxLength: 8
	}
).map(chars => chars.join(''));

/**
 * Generate arbitrary axis.
 */
export const arbAxis = fc.constantFrom('row', 'column') as fc.Arbitrary<'row' | 'column'>;

/**
 * Generate arbitrary priority.
 */
export const arbPriority = fc.constantFrom('high', 'normal', 'low') as fc.Arbitrary<
	'high' | 'normal' | 'low'
>;

/**
 * Layout operations for property-based testing.
 * Represents all possible operations that can be performed on a SplitPanel layout.
 */
export type LayoutOperation =
	| { type: 'addPanel'; panelId: string; position: 'first' | 'last' }
	| { type: 'removePanel'; panelId: string }
	| { type: 'toggleCollapse'; panelId: string }
	| { type: 'resizeDivider'; dividerIndex: number; delta: number }
	| { type: 'setContainerSize'; width: number; height: number }
	| { type: 'undo' }
	| { type: 'redo' };

/**
 * Generate arbitrary layout operations.
 * Takes existing panel IDs to ensure operations like remove/toggle are valid.
 *
 * @param existingPanelIds - Panel IDs that currently exist in the layout
 * @returns Arbitrary that generates valid operations
 */
export const arbOperation = (existingPanelIds: string[]): fc.Arbitrary<LayoutOperation> => {
	const operations: fc.Arbitrary<LayoutOperation>[] = [
		// Add panel with new ID
		fc.record({
			type: fc.constant('addPanel' as const),
			panelId: arbPanelId,
			position: fc.constantFrom('first', 'last') as fc.Arbitrary<'first' | 'last'>
		}),

		// Remove existing panel (if any)
		...(existingPanelIds.length > 0
			? [
					fc.record({
						type: fc.constant('removePanel' as const),
						panelId: fc.constantFrom(...existingPanelIds)
					})
				]
			: []),

		// Toggle collapse on existing panel (if any)
		...(existingPanelIds.length > 0
			? [
					fc.record({
						type: fc.constant('toggleCollapse' as const),
						panelId: fc.constantFrom(...existingPanelIds)
					})
				]
			: []),

		// Resize divider
		fc.record({
			type: fc.constant('resizeDivider' as const),
			dividerIndex: fc.nat({ max: 10 }),
			delta: fc.integer({ min: -200, max: 200 })
		}),

		// Container resize
		fc.record({
			type: fc.constant('setContainerSize' as const),
			width: fc.integer({ min: 200, max: 2000 }),
			height: fc.integer({ min: 200, max: 1500 })
		}),

		// Undo/redo
		fc.constant({ type: 'undo' as const }),
		fc.constant({ type: 'redo' as const })
	];

	return fc.oneof(...operations);
};

/**
 * Generate a sequence of operations.
 * Useful for testing sequences of random operations.
 *
 * @param initialPanelIds - Panel IDs that exist at the start
 * @param length - Number of operations to generate
 * @returns Arbitrary that generates operation sequences
 */
export const arbOperationSequence = (
	initialPanelIds: string[],
	length: number
): fc.Arbitrary<LayoutOperation[]> => {
	return fc.array(arbOperation(initialPanelIds), { minLength: length, maxLength: length });
};

/**
 * Generate arbitrary dimensions for testing container resizes.
 */
export const arbDimensions = fc.record({
	width: fc.integer({ min: 200, max: 2000 }),
	height: fc.integer({ min: 200, max: 1500 })
});

/**
 * Generate arbitrary panel configuration for testing panel creation.
 */
export const arbPanelConfig = fc.record({
	id: arbPanelId,
	size: fc.integer({ min: 50, max: 500 }),
	minSize: fc.option(fc.integer({ min: 20, max: 200 }), { nil: undefined }),
	maxSize: fc.option(fc.integer({ min: 300, max: 1000 }), { nil: undefined })
});

/**
 * Generate arbitrary resize delta for testing divider resizing.
 */
export const arbResizeDelta = fc.integer({ min: -500, max: 500 });

/**
 * Generate arbitrary divider index for testing.
 */
export const arbDividerIndex = fc.nat({ max: 10 });
