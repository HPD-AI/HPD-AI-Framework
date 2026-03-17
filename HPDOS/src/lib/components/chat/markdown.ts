import { Marked } from 'marked';
import { createHighlighter, type Highlighter } from 'shiki';
import markedShiki from 'marked-shiki';

let marked: Marked | null = null;
let initPromise: Promise<void> | null = null;

async function init() {
	const highlighter: Highlighter = await createHighlighter({
		themes: ['github-dark'],
		langs: [
			'typescript', 'javascript', 'tsx', 'jsx',
			'python', 'rust', 'go', 'java', 'csharp',
			'bash', 'sh', 'json', 'yaml', 'toml',
			'html', 'css', 'svelte', 'markdown', 'sql',
		],
	});

	marked = new Marked();
	marked.use(
		markedShiki({
			highlight(code, lang, props) {
				return highlighter.codeToHtml(code, {
					lang: lang || 'text',
					theme: 'github-dark',
					meta: { __raw: props.join(' ') },
				});
			},
		}),
	);
}

function ensureInit(): Promise<void> {
	if (!initPromise) initPromise = init();
	return initPromise;
}

export async function renderMarkdown(content: string): Promise<string> {
	await ensureInit();
	return marked!.parse(content) as string;
}
