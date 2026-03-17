/**
 * link-peers.ts
 *
 * Symlinks shared peer dependencies from the root node_modules into
 * file:-linked packages that have their own node_modules present.
 *
 * Required because Bun does not support cross-directory workspaces, so
 * file:-linked packages from sibling repos can end up with a nested copy
 * of peer deps (e.g. svelte), causing dual-instance runtime crashes.
 *
 * Run automatically via postinstall.
 */

import { symlinkSync, existsSync, rmSync } from 'fs';
import { resolve } from 'path';

const root = import.meta.dir + '/..';

const peers: { pkg: string; dep: string }[] = [
	{ pkg: 'node_modules/@hpd/hpd-agent-headless-ui', dep: 'svelte' },
];

for (const { pkg, dep } of peers) {
	const source = resolve(root, 'node_modules', dep);
	const target = resolve(root, pkg, 'node_modules', dep);

	if (!existsSync(source)) {
		console.warn(`[link-peers] source not found, skipping: ${source}`);
		continue;
	}

	// Remove existing dir or symlink before relinking
	if (existsSync(target)) {
		rmSync(target, { recursive: true, force: true });
	}

	symlinkSync(source, target);
	console.log(`[link-peers] ${pkg}/node_modules/${dep} → ${source}`);
}
