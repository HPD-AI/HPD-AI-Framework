/**
 * Theme Management with Native CSS light-dark() + View Transitions API
 *
 * Leverages browser-native theme switching with smooth animated transitions.
 * - Chrome 123+, Firefox 120+, Safari 17.5+ for light-dark()
 * - Chrome 111+, Safari 18+ for View Transitions (progressive enhancement)
 */

export type ColorScheme = 'auto' | 'light' | 'dark' | 'light-blue' | 'dark-teal' | 'light-purple' | 'dark-purple';

/**
 * Set the color scheme with smooth View Transition animation
 */
export function setColorScheme(scheme: ColorScheme): void {
	// Progressive enhancement - use View Transitions if supported
	if (document.startViewTransition) {
		document.startViewTransition(() => {
			updateColorScheme(scheme);
		});
	} else {
		updateColorScheme(scheme);
	}
}

/**
 * Internal function to actually update the color scheme
 */
function updateColorScheme(scheme: ColorScheme): void {
	const html = document.documentElement;

	// Update the meta tag for color-scheme
	const meta = document.querySelector('meta[name="color-scheme"]');
	if (meta) {
		meta.setAttribute('content', scheme);
	} else {
		// Create meta tag if it doesn't exist
		const newMeta = document.createElement('meta');
		newMeta.name = 'color-scheme';
		newMeta.content = scheme;
		document.head.appendChild(newMeta);
	}

	// Update data attribute for custom styling if needed
	html.setAttribute('data-color-scheme', scheme);

	// Persist to localStorage
	try {
		localStorage.setItem('color-scheme', scheme);
	} catch (error) {
		console.warn('Failed to save color scheme preference:', error);
	}
}

/**
 * Get the current color scheme
 */
export function getColorScheme(): ColorScheme {
	try {
		const saved = localStorage.getItem('color-scheme') as ColorScheme | null;
		if (saved) return saved;
	} catch (error) {
		console.warn('Failed to load color scheme preference:', error);
	}
	return 'auto'; // Default to system preference
}

/**
 * Initialize theme system with system preference detection
 * Call this on app startup for proper theme initialization
 */
export function initializeTheme(): void {
	// Check for manual override first
	const stored = getColorScheme();

	if (stored && stored !== 'auto') {
		// User has a manual theme preference
		updateColorScheme(stored);
	} else {
		// Use system preference (auto mode) — CSS handles light-dark() automatically
		updateColorScheme('auto');
	}
}

/**
 * Load color scheme from localStorage on app init
 * @deprecated Use initializeTheme() instead for full system integration
 */
export function loadColorScheme(): void {
	const scheme = getColorScheme();
	updateColorScheme(scheme);
}

/**
 * Toggle between available themes
 */
export function toggleColorScheme(): ColorScheme {
	const current = getColorScheme();

	// Cycle through: auto -> dark-teal -> light-blue -> dark-purple -> light-purple -> auto
	const cycle: ColorScheme[] = ['auto', 'dark-teal', 'light-blue', 'dark-purple', 'light-purple'];
	const currentIndex = cycle.indexOf(current);
	const next = cycle[(currentIndex + 1) % cycle.length] ?? 'auto';

	setColorScheme(next);
	return next;
}

/**
 * Get all available color schemes
 */
export function getAvailableColorSchemes(): ColorScheme[] {
	return ['auto', 'dark-teal', 'light-blue', 'dark-purple', 'light-purple', 'light', 'dark'];
}
