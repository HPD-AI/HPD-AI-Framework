import {
  buildIntraLineDiffSegments,
  computeTextDiff,
  foldDiffContext,
  getDiffDisplayLines,
  pairDiffLinesForSplit,
  parseDiffPatch,
} from '../src/diff/index.js';
import { describe, expect, it } from 'vitest';

const patch = `--- a/example.ts
+++ b/example.ts
@@ -1,5 +1,6 @@
-function greet(name) {
-  console.log("Hello, " + name);
+function greet(name: string): void {
+  console.log(\`Hello, \${name}!\`);
 }
 
-greet("World");
+greet("World");
+greet("TypeScript");`;

describe('diff helpers', () => {
  it('parses unified patches into files and typed lines', () => {
    const files = parseDiffPatch(patch);

    expect(files).toHaveLength(1);
    expect(files[0]?.oldName).toBe('example.ts');
    expect(files[0]?.newName).toBe('example.ts');
    expect(files[0]?.additions).toBe(4);
    expect(files[0]?.deletions).toBe(3);
    expect(files[0]?.lines.map((line) => line.type)).toEqual([
      'del',
      'del',
      'add',
      'add',
      'normal',
      'normal',
      'del',
      'add',
      'add',
    ]);
  });

  it('computes old/new text diffs', () => {
    const file = computeTextDiff(
      { name: 'example.ts', content: 'let count = 1;\nconsole.log(count);\n' },
      { name: 'example.ts', content: 'const count = 2;\nconsole.log(count);\n' },
    );

    expect(file.additions).toBe(1);
    expect(file.deletions).toBe(1);
    expect(file.lines[0]).toMatchObject({ type: 'del', oldLineNumber: 1 });
    expect(file.lines[1]).toMatchObject({ type: 'add', newLineNumber: 1 });
    expect(file.lines[2]).toMatchObject({
      type: 'normal',
      oldLineNumber: 2,
      newLineNumber: 2,
    });
  });

  it('folds unchanged context around changed lines', () => {
    const file = computeTextDiff(
      { content: ['a', 'b', 'c', 'd', 'e', 'f'].join('\n') },
      { content: ['a', 'b', 'C', 'd', 'e', 'F'].join('\n') },
    );
    const displayLines = foldDiffContext(file.lines, 0);

    expect(displayLines).toContainEqual({ type: 'fold', hiddenCount: 2 });
    expect(displayLines.filter((line) => line.type !== 'fold')).toHaveLength(4);
  });

  it('truncates display lines', () => {
    const file = computeTextDiff(
      { content: 'a\nb\nc\nd\n' },
      { content: 'A\nB\nC\nD\n' },
    );
    const display = getDiffDisplayLines({ lines: file.lines, maxLines: 3 });

    expect(display.truncated).toBe(true);
    expect(display.lines).toHaveLength(3);
    expect(display.remainingCount).toBeGreaterThan(0);
  });

  it('pairs deletions and additions for split view', () => {
    const file = computeTextDiff(
      { content: 'let count = 1;\nconsole.log(count);\n' },
      { content: 'const count = 2;\nconsole.log(count);\n' },
    );
    const pairs = pairDiffLinesForSplit(file.lines);

    expect(pairs[0]?.left?.type).toBe('del');
    expect(pairs[0]?.right?.type).toBe('add');
    expect(pairs[1]?.left?.type).toBe('normal');
    expect(pairs[1]?.right?.type).toBe('normal');
  });

  it('builds intra-line segments', () => {
    const segments = buildIntraLineDiffSegments('const a = 1', 'const a = 2');

    expect(segments.delSegments.some((segment) => segment.changed && segment.text === '1')).toBe(true);
    expect(segments.addSegments.some((segment) => segment.changed && segment.text === '2')).toBe(true);
  });
});
