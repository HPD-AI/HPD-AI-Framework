declare const marked: { parse(value: string): string; setOptions(options: Record<string, unknown>): void };
declare const DOMPurify: { sanitize(value: string): string };

export function initializeMarkdown() {
  marked.setOptions({ gfm: true, breaks: true });
}

export function markdownHtml(value: string) {
  return DOMPurify.sanitize(marked.parse(value));
}
