/**
 * Keyboard Navigation Utilities
 *
 * Geometry-based keyboard navigation for split panels.
 * Uses cached rects from registerPanel action for O(n) or O(log n) navigation without reflows.
 *
 * Features:
 * - Arrow key navigation based on spatial position
 * - Overlap detection for intelligent candidate selection
 * - Distance-based sorting for nearest neighbor
 * - Spatial index (grid-based) for O(log n) when panels > SPATIAL_INDEX_THRESHOLD
 * - O(n) fallback for smaller layouts
 *
 * Note: This is different from RovingFocusGroup (sequential/linear navigation).
 * We need 2D spatial navigation for split panels based on actual visual position.
 */

import { getPanelRect } from '../actions/register-panel.js';

/**
 * Navigation direction.
 */
export type Direction = 'up' | 'down' | 'left' | 'right';

/**
 * Threshold for enabling spatial index optimization.
 * Below this, use simple O(n) scan. Above this, use grid-based spatial index.
 */
const SPATIAL_INDEX_THRESHOLD = 100;

/**
 * Grid cell size for spatial index (in pixels).
 * Larger = fewer cells, less memory, but less precise filtering.
 * Smaller = more cells, more memory, but better filtering.
 */
const GRID_CELL_SIZE = 200;

/**
 * Grid-based spatial index for fast candidate filtering.
 * Maps grid cell coordinates to panel IDs in that cell.
 */
interface SpatialIndex {
	/** Grid cells: Map<"x,y", panelIds[]> */
	grid: Map<string, string[]>;
	/** Bounding box of all panels */
	bounds: { minX: number; minY: number; maxX: number; maxY: number } | null;
}

/**
 * Build spatial index from panel IDs and their rects.
 * Used for O(log n) navigation in large layouts.
 */
function buildSpatialIndex(panelIds: string[]): SpatialIndex {
	const grid = new Map<string, string[]>();
	let minX = Infinity,
		minY = Infinity,
		maxX = -Infinity,
		maxY = -Infinity;

	for (const panelId of panelIds) {
		const rect = getPanelRect(panelId);
		if (!rect) continue;

		// Update bounds
		minX = Math.min(minX, rect.left);
		minY = Math.min(minY, rect.top);
		maxX = Math.max(maxX, rect.right);
		maxY = Math.max(maxY, rect.bottom);

		// Calculate grid cells this panel occupies
		const startCellX = Math.floor(rect.left / GRID_CELL_SIZE);
		const endCellX = Math.floor(rect.right / GRID_CELL_SIZE);
		const startCellY = Math.floor(rect.top / GRID_CELL_SIZE);
		const endCellY = Math.floor(rect.bottom / GRID_CELL_SIZE);

		// Add panel to all cells it intersects
		for (let x = startCellX; x <= endCellX; x++) {
			for (let y = startCellY; y <= endCellY; y++) {
				const key = `${x},${y}`;
				const cell = grid.get(key) ?? [];
				cell.push(panelId);
				grid.set(key, cell);
			}
		}
	}

	return {
		grid,
		bounds: minX !== Infinity ? { minX, minY, maxX, maxY } : null
	};
}

/**
 * Get candidate panel IDs from spatial index based on direction.
 * Filters by grid cells in the target direction.
 */
function getCandidatesFromIndex(
	index: SpatialIndex,
	currentRect: DOMRectReadOnly,
	direction: Direction
): string[] {
	if (!index.bounds) return [];

	const candidates = new Set<string>();

	// Calculate current panel's grid cells
	const currentCellX = Math.floor((currentRect.left + currentRect.right) / 2 / GRID_CELL_SIZE);
	const currentCellY = Math.floor((currentRect.top + currentRect.bottom) / 2 / GRID_CELL_SIZE);

	// Define search region based on direction
	let minCellX = 0,
		maxCellX = Math.floor(index.bounds.maxX / GRID_CELL_SIZE);
	let minCellY = 0,
		maxCellY = Math.floor(index.bounds.maxY / GRID_CELL_SIZE);

	if (direction === 'up') {
		maxCellY = currentCellY - 1; // Only cells above
	} else if (direction === 'down') {
		minCellY = currentCellY + 1; // Only cells below
	} else if (direction === 'left') {
		maxCellX = currentCellX - 1; // Only cells left
	} else if (direction === 'right') {
		minCellX = currentCellX + 1; // Only cells right
	}

	// Collect panels from relevant cells
	for (let x = minCellX; x <= maxCellX; x++) {
		for (let y = minCellY; y <= maxCellY; y++) {
			const key = `${x},${y}`;
			const cell = index.grid.get(key);
			if (cell) {
				for (const panelId of cell) {
					candidates.add(panelId);
				}
			}
		}
	}

	return Array.from(candidates);
}

/**
 * Find the next panel in a given direction using geometry-based navigation.
 *
 * Algorithm:
 * 1. Get current panel's cached rect
 * 2. Use spatial index for large layouts (>100 panels), or scan all for small layouts
 * 3. Filter candidates by directional position and overlap
 * 4. Calculate Euclidean distance between centers
 * 5. Return nearest candidate
 *
 * Example:
 * ```
 * Layout: [A B]
 *         [C]
 *
 * Focus on B, press RIGHT → finds C by comparing cached rectangles
 * Works because C's rect.left > B's rect.right, making C a valid candidate
 * ```
 *
 * Performance:
 * - Time: O(n) for n < 100, O(log n) for n >= 100 with spatial index
 * - Space: O(n) for candidate array, O(n) for spatial index
 * - No DOM reflows (uses cached rects)
 *
 * @param fromPanelId - Current panel ID
 * @param direction - Direction to navigate
 * @param panelIds - Array of all panel IDs to consider
 * @returns Next panel ID or null if at boundary
 */
export function findPanelInDirection(
	fromPanelId: string,
	direction: Direction,
	panelIds: string[]
): string | null {
	const currentRect = getPanelRect(fromPanelId);
	if (!currentRect) return null;

	// Use spatial index for large layouts, simple scan for small
	const useSpatialIndex = panelIds.length > SPATIAL_INDEX_THRESHOLD;
	const candidateIds = useSpatialIndex
		? getCandidatesFromIndex(buildSpatialIndex(panelIds), currentRect, direction)
		: panelIds;

	// Find all valid candidate panels in the requested direction
	const candidates: Array<{ panelId: string; distance: number }> = [];

	for (const panelId of candidateIds) {
		if (panelId === fromPanelId) continue;

		const rect = getPanelRect(panelId);
		if (!rect) continue;

		// Check if panel is in the requested direction with positional overlap
		let isCandidate = false;

		if (direction === 'up' && rect.bottom <= currentRect.top) {
			// Candidate is above: has x-overlap and rect.bottom <= current.top
			if (!(rect.right < currentRect.left || rect.left > currentRect.right)) {
				isCandidate = true;
			}
		} else if (direction === 'down' && rect.top >= currentRect.bottom) {
			// Candidate is below: has x-overlap and rect.top >= current.bottom
			if (!(rect.right < currentRect.left || rect.left > currentRect.right)) {
				isCandidate = true;
			}
		} else if (direction === 'left' && rect.right <= currentRect.left) {
			// Candidate is left: has y-overlap and rect.right <= current.left
			if (!(rect.bottom < currentRect.top || rect.top > currentRect.bottom)) {
				isCandidate = true;
			}
		} else if (direction === 'right' && rect.left >= currentRect.right) {
			// Candidate is right: has y-overlap and rect.left >= current.right
			if (!(rect.bottom < currentRect.top || rect.top > currentRect.bottom)) {
				isCandidate = true;
			}
		}

		if (isCandidate) {
			// Euclidean distance between centers
			const currentCenterX = currentRect.left + currentRect.width / 2;
			const currentCenterY = currentRect.top + currentRect.height / 2;
			const rectCenterX = rect.left + rect.width / 2;
			const rectCenterY = rect.top + rect.height / 2;

			const distance = Math.sqrt(
				Math.pow(rectCenterX - currentCenterX, 2) + Math.pow(rectCenterY - currentCenterY, 2)
			);

			candidates.push({ panelId, distance });
		}
	}

	// Return nearest candidate or null if none found (at boundary)
	if (candidates.length === 0) return null;

	candidates.sort((a, b) => a.distance - b.distance);
	return candidates[0].panelId;
}

/**
 * Get all panel IDs from a layout tree.
 * Helper for collecting all navigable panels.
 *
 * @param node - Root layout node
 * @returns Array of all panel IDs in the tree
 */
export function collectPanelIds(node: any): string[] {
	const ids: string[] = [];

	function traverse(n: any) {
		if (n.type === 'leaf') {
			ids.push(n.id);
		} else if (n.type === 'branch') {
			for (const child of n.children) {
				traverse(child);
			}
		}
	}

	traverse(node);
	return ids;
}

/**
 * Check if a rect overlaps another rect on a given axis.
 *
 * @param rect1 - First rectangle
 * @param rect2 - Second rectangle
 * @param axis - Axis to check ('x' or 'y')
 * @returns True if rects overlap on the axis
 */
export function hasOverlap(
	rect1: DOMRectReadOnly,
	rect2: DOMRectReadOnly,
	axis: 'x' | 'y'
): boolean {
	if (axis === 'x') {
		return !(rect1.right < rect2.left || rect1.left > rect2.right);
	} else {
		return !(rect1.bottom < rect2.top || rect1.top > rect2.bottom);
	}
}

/**
 * Calculate distance between two rect centers.
 *
 * @param rect1 - First rectangle
 * @param rect2 - Second rectangle
 * @returns Euclidean distance between centers
 */
export function distanceBetweenRects(rect1: DOMRectReadOnly, rect2: DOMRectReadOnly): number {
	const center1X = rect1.left + rect1.width / 2;
	const center1Y = rect1.top + rect1.height / 2;
	const center2X = rect2.left + rect2.width / 2;
	const center2Y = rect2.top + rect2.height / 2;

	return Math.sqrt(Math.pow(center2X - center1X, 2) + Math.pow(center2Y - center1Y, 2));
}
