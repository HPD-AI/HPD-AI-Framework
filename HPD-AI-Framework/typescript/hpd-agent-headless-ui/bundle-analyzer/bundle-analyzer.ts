/**
 * Bundle Size Analyzer - Per-Component Size Tracking
 *
 * Tracks each component's bundle size to ensure we stay under our targets.
 *
 * Size Targets (gzipped):
 * - Total library: < 20 KB
 * - createAgent (core): < 5 KB
 * - Message: < 2 KB
 * - ToolExecution: < 3 KB
 * - PermissionDialog: < 2 KB
 * - ClarificationDialog: < 2 KB
 * - Input: < 1 KB
 * - MessageList: < 2 KB
 *
 * Run with: bun run analyze
 */

import { build } from 'vite';
import { gzipSync } from 'zlib';
import { readFileSync, existsSync } from 'fs';
import { join } from 'path';

// ========================================
// Size Limits (bytes, gzipped)
// ========================================

interface ComponentSizeLimit {
	name: string;
	path: string;
	limit: number; // bytes (gzipped)
}

const COMPONENT_SIZE_LIMITS: ComponentSizeLimit[] = [
	{ name: 'createAgent (core)', path: 'bits/agent/index.ts', limit: 5000 },
	{ name: 'Message', path: 'bits/message/index.ts', limit: 2000 },
	{ name: 'MessageList', path: 'bits/message-list/index.ts', limit: 2000 },
	{ name: 'ToolExecution', path: 'bits/tool-execution/index.ts', limit: 3000 },
	{ name: 'PermissionDialog', path: 'bits/permission-dialog/index.ts', limit: 2000 },
	{ name: 'ClarificationDialog', path: 'bits/clarification-dialog/index.ts', limit: 2000 },
	{ name: 'Input', path: 'bits/input/index.ts', limit: 1000 }
];

const TOTAL_LIBRARY_LIMIT = 20000; // 20 KB

// ========================================
// Utilities
// ========================================

function formatBytes(bytes: number): string {
	return `${(bytes / 1024).toFixed(2)} KB`;
}

function formatPercentage(value: number, max: number): string {
	return `${((value / max) * 100).toFixed(1)}%`;
}

// ========================================
// Component Analysis
// ========================================

async function analyzeComponent(component: ComponentSizeLimit): Promise<{
	name: string;
	size: number;
	gzipped: number;
	limit: number;
	withinLimit: boolean;
}> {
	const entryPath = join(process.cwd(), 'src/lib', component.path);

	// Check if component exists
	if (!existsSync(entryPath)) {
		console.warn(` Component not found: ${component.name} (${entryPath})`);
		return {
			name: component.name,
			size: 0,
			gzipped: 0,
			limit: component.limit,
			withinLimit: true // Don't fail on missing components (not built yet)
		};
	}

	try {
		// Build component in isolation
		const result = await build({
			configFile: false,
			logLevel: 'silent',
			build: {
				lib: {
					entry: entryPath,
					formats: ['es']
				},
				write: false,
				minify: 'terser', // Production-level minification
				rollupOptions: {
					external: ['svelte', '@hpd/hpd-agent-client']
				}
			}
		});

		// Get output
		const output = Array.isArray(result) ? result[0].output[0] : result.output[0];
		const code = 'code' in output ? output.code : '';

		// Calculate sizes
		const size = Buffer.byteLength(code);
		const gzipped = gzipSync(code).length;

		return {
			name: component.name,
			size,
			gzipped,
			limit: component.limit,
			withinLimit: gzipped <= component.limit
		};
	} catch (error) {
		console.error(`  Error analyzing ${component.name}:`, error);
		throw error;
	}
}

// ========================================
// Main Analysis
// ========================================

async function analyzeAllComponents() {
	console.log('📊 HPD Agent Headless UI - Bundle Size Analysis\n');
	console.log('═'.repeat(80));
	console.log();

	const results = [];
	let totalGzipped = 0;
	let hasFailures = false;

	// Analyze each component
	for (const component of COMPONENT_SIZE_LIMITS) {
		const result = await analyzeComponent(component);
		results.push(result);

		if (result.gzipped > 0) {
			totalGzipped += result.gzipped;

			// Print result
			const status = result.withinLimit ? ' ' : ' ';
			const sizePct = formatPercentage(result.gzipped, result.limit);

			console.log(`${status} ${result.name}`);
			console.log(`   Size: ${formatBytes(result.gzipped)} (${sizePct} of limit)`);
			console.log(`   Limit: ${formatBytes(result.limit)}`);

			if (!result.withinLimit) {
				const overage = result.gzipped - result.limit;
				console.log(`    EXCEEDS LIMIT by ${formatBytes(overage)}`);
				hasFailures = true;
			}

			console.log();
		}
	}

	// Total library size
	console.log('═'.repeat(80));
	console.log();
	console.log('📦 Total Library Size');
	console.log(`   Combined: ${formatBytes(totalGzipped)}`);
	console.log(`   Limit: ${formatBytes(TOTAL_LIBRARY_LIMIT)}`);

	const totalWithinLimit = totalGzipped <= TOTAL_LIBRARY_LIMIT;
	const totalPct = formatPercentage(totalGzipped, TOTAL_LIBRARY_LIMIT);

	if (totalWithinLimit) {
		console.log(`    WITHIN LIMIT (${totalPct})`);
	} else {
		const overage = totalGzipped - TOTAL_LIBRARY_LIMIT;
		console.log(`     EXCEEDS LIMIT by ${formatBytes(overage)} (${totalPct})`);
		hasFailures = true;
	}

	console.log();
	console.log('═'.repeat(80));
	console.log();

	// Summary
	const builtComponents = results.filter((r) => r.gzipped > 0).length;
	const totalComponents = COMPONENT_SIZE_LIMITS.length;

	console.log(`📈 Summary`);
	console.log(`   Components Analyzed: ${builtComponents}/${totalComponents}`);
	console.log(`   Total Size: ${formatBytes(totalGzipped)}`);
	console.log(`   Size Limit: ${formatBytes(TOTAL_LIBRARY_LIMIT)}`);
	console.log(
		`   Budget Used: ${formatPercentage(totalGzipped, TOTAL_LIBRARY_LIMIT)}`
	);
	console.log();

	// Exit with error if any failures
	if (hasFailures) {
		console.error('  Bundle size analysis FAILED - components exceed limits\n');
		process.exit(1);
	} else {
		console.log('  Bundle size analysis PASSED - all components within limits\n');
	}
}

// ========================================
// Run Analysis
// ========================================

analyzeAllComponents().catch((error) => {
	console.error('  Bundle analysis failed:', error);
	process.exit(1);
});
