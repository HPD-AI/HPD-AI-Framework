/**
 * SplitPanel Error Handling Types
 *
 * Discriminated union for layout operation errors with explicit error handling.
 * All layout mutations return Result instead of throwing exceptions.
 */

/**
 * Discriminated union for layout operation errors.
 * Enables exhaustive error handling with type safety.
 */
export type LayoutError =
	| { type: 'not-found'; id: string }
	| { type: 'invalid-parent'; message: string }
	| { type: 'invalid-path'; path: number[] }
	| { type: 'not-a-branch'; path: number[] }
	| { type: 'invalid-divider'; index: number; validRange: string }
	| { type: 'layout-in-progress'; message: string }
	| { type: 'constraint-violation'; message: string }
	| { type: 'duplicate-id'; id: string };

/**
 * Result type for explicit error handling.
 * All layout mutations return Result instead of throwing exceptions.
 */
export type Result<T, E = LayoutError> = { ok: true; value: T } | { ok: false; error: E };

/**
 * Helper to create a successful result
 */
export function Ok<T>(value: T): Result<T> {
	return { ok: true, value };
}

/**
 * Helper to create an error result
 */
export function Err<E = LayoutError>(error: E): Result<never, E> {
	return { ok: false, error };
}
