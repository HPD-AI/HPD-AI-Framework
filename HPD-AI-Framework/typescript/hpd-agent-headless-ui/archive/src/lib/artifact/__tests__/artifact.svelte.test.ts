/**
 * Artifact Browser Test
 *
 * Browser tests for the Artifact component.
 * Tests core functionality: rendering, open/close, content teleportation, multiple artifacts.
 */

import { describe, it, expect, vi } from 'vitest';
import { render } from 'vitest-browser-svelte';
import { page } from 'vitest/browser';
import ArtifactTest from './artifact-test.svelte';

describe('Artifact', () => {
	it('should render provider', async () => {
		render(ArtifactTest, { props: {} } as any);
		const provider = page.getByTestId('provider');
		await expect.element(provider).toBeInTheDocument();
	});

	it('should render root', async () => {
		render(ArtifactTest, { props: {} } as any);
		const root = page.getByTestId('root-artifact-1');
		await expect.element(root).toBeInTheDocument();
	});

	it('should render trigger', async () => {
		render(ArtifactTest, { props: {} } as any);
		const trigger = page.getByTestId('trigger-artifact-1');
		await expect.element(trigger).toBeInTheDocument();
	});

	it('should open panel when trigger is clicked', async () => {
		render(ArtifactTest, { props: {} } as any);
		const trigger = page.getByTestId('trigger-artifact-1');
		const panel = page.getByTestId('panel');

		// Panel should not exist initially
		await expect.element(panel).not.toBeInTheDocument();

		// Click trigger
		await trigger.click();

		// Panel should now exist
		await expect.element(panel).toBeInTheDocument();
	});

	it('should render title snippet in panel', async () => {
		render(ArtifactTest, { props: {} } as any);
		const trigger = page.getByTestId('trigger-artifact-1');
		await trigger.click();

		const titleContent = page.getByTestId('title-content');
		await expect.element(titleContent).toHaveTextContent('Test Title');
	});

	it('should close panel when close button is clicked', async () => {
		render(ArtifactTest, { props: {} } as any);
		const trigger = page.getByTestId('trigger-artifact-1');
		await trigger.click();

		const close = page.getByTestId('close');
		await close.click();

		const panel = page.getByTestId('panel');
		await expect.element(panel).not.toBeInTheDocument();
	});

	it('should call onProviderOpenChange', async () => {
		const onProviderOpenChange = vi.fn();
		render(ArtifactTest, { props: { onProviderOpenChange } } as any);

		const trigger = page.getByTestId('trigger-artifact-1');
		await trigger.click();

		expect(onProviderOpenChange).toHaveBeenCalledWith(true, 'artifact-1');
	});

	it('should handle multiple artifacts with #each', async () => {
		render(ArtifactTest, { props: { artifactIds: ['a', 'b', 'c'] } } as any);

		const rootA = page.getByTestId('root-a');
		const rootB = page.getByTestId('root-b');
		const rootC = page.getByTestId('root-c');

		await expect.element(rootA).toBeInTheDocument();
		await expect.element(rootB).toBeInTheDocument();
		await expect.element(rootC).toBeInTheDocument();
	});

	it('should only allow one artifact open at a time', async () => {
		render(ArtifactTest, { props: { artifactIds: ['a', 'b'] } } as any);

		const triggerA = page.getByTestId('trigger-a');
		const triggerB = page.getByTestId('trigger-b');

		await triggerA.click();
		await expect.element(page.getByTestId('root-a')).toHaveAttribute('data-open');

		await triggerB.click();
		await expect.element(page.getByTestId('root-a')).not.toHaveAttribute('data-open');
		await expect.element(page.getByTestId('root-b')).toHaveAttribute('data-open');
	});
});
