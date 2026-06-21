import type { ToolCallKind } from '../types/acp.js';

/**
 * Maps HPD tool names to ACP ToolCallKind values.
 * Editors use the kind to select display icons and affordances.
 */
const KIND_MAP: Array<[RegExp, ToolCallKind]> = [
  [/read.?file|get.?file|view.?file|cat.?file|open.?file/i,  'read'],
  [/write.?file|create.?file|edit.?file|str.?replace|patch.?file|update.?file/i, 'edit'],
  [/delete.?file|remove.?file/i, 'delete'],
  [/move.?file|rename.?file/i,   'move'],
  [/search|grep|glob|find.?file|ripgrep|rg\b/i, 'search'],
  [/bash|shell|run.?command|execute|terminal|cmd\b/i, 'execute'],
  [/web.?fetch|http.?request|fetch.?url|browse/i, 'fetch'],
  [/think|plan|reason|reflect/i, 'think'],
  [/switch.?mode|set.?mode/i,    'switch_mode'],
];

export function toolNameToKind(toolName: string): ToolCallKind {
  for (const [pattern, kind] of KIND_MAP) {
    if (pattern.test(toolName)) return kind;
  }
  return 'other';
}
