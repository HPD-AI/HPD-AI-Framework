<script lang="ts">
	import Input from '../components/input.svelte';
	import type { InputProps } from '../types.js';

	let {
		value = $bindable(),
		defaultValue = '',
		onChange,
		onSubmit,
		disabled = false,
		maxRows = 5,
		placeholder = 'Type a message...',
		autoFocus = false,
		name,
		required = false,
		'aria-label': ariaLabel = 'Message input',
		...restProps
	}: InputProps = $props();
</script>

<main data-testid="main">
	<Input
		data-testid="input"
		bind:value
		{defaultValue}
		{onChange}
		{onSubmit}
		{disabled}
		{maxRows}
		{placeholder}
		{autoFocus}
		{name}
		{required}
		aria-label={ariaLabel}
		{...restProps}
	/>
</main>
