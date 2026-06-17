/**
 * Creates a unique ID for components with optional prefix
 */

let counter = 0;

export function createId(uid?: string): string;
export function createId(prefix: string, uid: string): string;
export function createId(prefixOrUid?: string, uid?: string): string {
	if (uid !== undefined) {
		// Two-argument form: createId(prefix, uid)
		return `hpd-${prefixOrUid}-${uid}`;
	}
	if (prefixOrUid !== undefined) {
		// One-argument form: createId(uid)
		return `hpd-${prefixOrUid}`;
	}
	// No arguments: generate unique ID
	return `hpd-${++counter}`;
}
