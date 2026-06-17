<script module>
	import { defineMeta } from '@storybook/addon-svelte-csf';
	import ArtifactDemo from './ArtifactDemo.svelte';

	const { Story } = defineMeta({
		title: 'Components/Artifact',
		component: ArtifactDemo,
		tags: ['autodocs'],
		argTypes: {
			artifacts: {
				control: 'object',
				description: 'Array of artifacts to display'
			},
			defaultOpenId: {
				control: 'text',
				description: 'ID of artifact to open by default'
			},
			showPanel: {
				control: 'boolean',
				description: 'Whether to show the side panel'
			}
		},
		parameters: {
			docs: {
				description: {
					component: `
The Artifact component enables AI-generated content to be rendered in a dedicated side panel.
It uses content "teleportation" - content defined in one place (Artifact.Slot) renders elsewhere (Artifact.Panel).

## Features
- **Provider/Consumer Pattern**: Single provider manages all artifacts
- **Content Teleportation**: Define content inline, render in panel
- **Single Open**: Only one artifact can be open at a time
- **Animation Support**: Built-in presence management for enter/exit animations

## Components
- \`Artifact.Provider\` - Wraps the entire artifact system
- \`Artifact.Root\` - Individual artifact container with unique ID
- \`Artifact.Slot\` - Registers title/content snippets
- \`Artifact.Trigger\` - Button to open/close the artifact
- \`Artifact.Panel\` - Side panel that renders the open artifact
- \`Artifact.Title\` - Renders the title snippet
- \`Artifact.Content\` - Renders the content snippet
- \`Artifact.Close\` - Button to close the panel
					`
				}
			}
		}
	});
</script>

<!-- Default Story - Single Code Artifact -->
<Story
	name="Default"
	args={{
		artifacts: [
			{
				id: 'code-1',
				title: 'Hello World',
				type: 'code',
				content: 'console.log("Hello, World!");',
				language: 'javascript'
			}
		],
		showPanel: true
	}}
/>

<!-- Multiple Artifacts -->
<Story
	name="Multiple Artifacts"
	args={{
		artifacts: [
			{
				id: 'code-1',
				title: 'React Component',
				type: 'code',
				content: `function Button({ children, onClick }) {
  return (
    <button onClick={onClick}>
      {children}
    </button>
  );
}`,
				language: 'jsx'
			},
			{
				id: 'code-2',
				title: 'CSS Styles',
				type: 'code',
				content: `.button {
  padding: 0.5rem 1rem;
  border-radius: 4px;
  background: #4a90d9;
  color: white;
}`,
				language: 'css'
			},
			{
				id: 'doc-1',
				title: 'Documentation',
				type: 'document',
				content:
					'This button component accepts children and an onClick handler. It renders a styled button element with the provided content.'
			}
		],
		showPanel: true
	}}
/>

<!-- With Default Open -->
<Story
	name="Default Open"
	args={{
		artifacts: [
			{
				id: 'artifact-open',
				title: 'Pre-opened Artifact',
				type: 'code',
				content: '// This artifact opens automatically\nconst ready = true;',
				language: 'javascript'
			}
		],
		defaultOpenId: 'artifact-open',
		showPanel: true
	}}
/>

<!-- Different Artifact Types -->
<Story
	name="Different Types"
	args={{
		artifacts: [
			{
				id: 'type-code',
				title: 'Code Example',
				type: 'code',
				content: 'const x = 42;',
				language: 'typescript'
			},
			{
				id: 'type-doc',
				title: 'Project README',
				type: 'document',
				content:
					'This is a sample project documentation. It explains how to use the artifact component system for displaying AI-generated content.'
			},
			{
				id: 'type-image',
				title: 'Architecture Diagram',
				type: 'image',
				content: '[Image placeholder - In real usage, this would contain an image or SVG]'
			},
			{
				id: 'type-chart',
				title: 'Performance Metrics',
				type: 'chart',
				content: '[Chart placeholder - In real usage, this would render a chart component]'
			}
		],
		showPanel: true
	}}
/>

<!-- Long Code Content -->
<Story
	name="Long Content"
	args={{
		artifacts: [
			{
				id: 'long-code',
				title: 'Full Component',
				type: 'code',
				content: `import { useState, useEffect } from 'react';

interface User {
  id: string;
  name: string;
  email: string;
}

export function UserList() {
  const [users, setUsers] = useState<User[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    async function fetchUsers() {
      try {
        const response = await fetch('/api/users');
        if (!response.ok) throw new Error('Failed to fetch');
        const data = await response.json();
        setUsers(data);
      } catch (err) {
        setError(err.message);
      } finally {
        setLoading(false);
      }
    }
    fetchUsers();
  }, []);

  if (loading) return <div>Loading...</div>;
  if (error) return <div>Error: {error}</div>;

  return (
    <ul>
      {users.map(user => (
        <li key={user.id}>
          {user.name} ({user.email})
        </li>
      ))}
    </ul>
  );
}`,
				language: 'tsx'
			}
		],
		showPanel: true
	}}
/>

<!-- Without Panel (Triggers Only) -->
<Story
	name="Triggers Only"
	args={{
		artifacts: [
			{
				id: 'no-panel-1',
				title: 'Artifact 1',
				type: 'code',
				content: 'console.log("1");'
			},
			{
				id: 'no-panel-2',
				title: 'Artifact 2',
				type: 'document',
				content: 'Document content'
			}
		],
		showPanel: false
	}}
/>
