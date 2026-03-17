/**
 * Debounce utility for internal component use
 *
 * Creates a debounced function that delays execution until after `wait` milliseconds
 * have elapsed since the last time it was invoked. Includes a `.destroy()` method
 * for cleanup to prevent memory leaks.
 *
 * @internal - This is for internal library use only. Consumers should implement
 * their own debouncing for component callbacks like onChange.
 *
 * @example
 * ```typescript
 * // Internal component usage:
 * const handleResize = debounce(() => {
 *   updateLayout();
 * }, 250);
 *
 * // Cleanup on component destroy:
 * onDestroyEffect(() => {
 *   handleResize.destroy();
 * });
 * ```
 */
export function debounce<T extends (...args: any[]) => any>(fn: T, wait = 500) {
	let timeout: NodeJS.Timeout | null = null;

	const debounced = (...args: Parameters<T>) => {
		if (timeout !== null) {
			clearTimeout(timeout);
		}
		timeout = setTimeout(() => {
			fn(...args);
		}, wait);
	};

	/**
	 * Cleanup method to cancel pending execution and clear timeout.
	 * MUST be called when component is destroyed to prevent memory leaks.
	 */
	debounced.destroy = () => {
		if (timeout !== null) {
			clearTimeout(timeout);
			timeout = null;
		}
	};

	return debounced;
}
