<script context="module">
	import { defineMeta } from '@storybook/addon-svelte-csf';
	import PermissionDialogRenderDemo from './PermissionDialogRenderDemo.svelte';

	const { Story } = defineMeta({
		title: 'Components/PermissionDialog',
		component: PermissionDialogRenderDemo,
		tags: ['autodocs']
	});
</script>

<Story name="Custom Render  " />
