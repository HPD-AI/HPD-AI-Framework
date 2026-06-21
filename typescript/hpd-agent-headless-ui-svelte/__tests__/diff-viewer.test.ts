import { mount, unmount } from 'svelte';
import { describe, expect, it } from 'vitest';
import DiffViewer from '../src/diff-viewer/diff-viewer.svelte';

const patch = `diff --git a/example.ts b/example.ts
index 1111111..2222222 100644
--- a/example.ts
+++ b/example.ts
@@ -1,5 +1,6 @@
 export function greet(name: string) {
-  return 'Hello ' + name;
+  const message = 'Hello ' + name;
+  return message;
 }
 
 greet('HPD');
`;

function mountTarget(): HTMLElement {
  const target = document.createElement('div');
  document.body.append(target);
  return target;
}

describe('DiffViewer', () => {
  it('renders a unified patch with file stats and line hooks', () => {
    const target = mountTarget();
    const component = mount(DiffViewer, {
      target,
      props: { patch },
    });

    expect(target.querySelector('[data-hpd-diff-viewer]')).not.toBeNull();
    expect(target.querySelector('[data-hpd-diff-file-name]')?.textContent).toBe('example.ts');
    expect(target.querySelector('[data-hpd-diff-additions]')?.textContent).toContain('+2');
    expect(target.querySelector('[data-hpd-diff-deletions]')?.textContent).toContain('-1');
    expect(target.querySelectorAll('[data-hpd-diff-line][data-line-type="add"]')).toHaveLength(2);
    expect(target.querySelectorAll('[data-hpd-diff-line][data-line-type="del"]')).toHaveLength(1);

    unmount(component);
    target.remove();
  });

  it('renders split view sides', () => {
    const target = mountTarget();
    const component = mount(DiffViewer, {
      target,
      props: {
        patch,
        viewMode: 'split',
      },
    });

    expect(target.querySelector('[data-hpd-diff-viewer]')?.getAttribute('data-view-mode')).toBe('split');
    expect(target.querySelectorAll('[data-hpd-diff-split-line]').length).toBeGreaterThan(0);
    expect(target.querySelector('[data-hpd-diff-split-side][data-side="left"]')).not.toBeNull();
    expect(target.querySelector('[data-hpd-diff-split-side][data-side="right"]')).not.toBeNull();

    unmount(component);
    target.remove();
  });

  it('can compute a diff from old and new file contents', () => {
    const target = mountTarget();
    const component = mount(DiffViewer, {
      target,
      props: {
        oldFile: { name: 'agent.ts', content: 'const ready = false;\n' },
        newFile: { name: 'agent.ts', content: 'const ready = true;\n' },
      },
    });

    expect(target.textContent).toContain('agent.ts');
    expect(target.textContent).toContain('const ready = false;');
    expect(target.textContent).toContain('const ready = true;');

    unmount(component);
    target.remove();
  });

  it('supports context folding', () => {
    const target = mountTarget();
    const component = mount(DiffViewer, {
      target,
      props: {
        patch,
        contextLines: 0,
      },
    });

    expect(target.querySelector('[data-hpd-diff-fold]')).not.toBeNull();

    unmount(component);
    target.remove();
  });
});
