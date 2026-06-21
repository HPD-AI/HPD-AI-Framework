import type { Meta, StoryObj } from '@storybook/svelte-vite';
import ToolCallDemo from './ToolCallDemo.svelte';

const meta = {
  title: 'Thread/ToolCall',
  component: ToolCallDemo,
  tags: ['autodocs'],
  argTypes: {
    renderMode: {
      control: { type: 'inline-radio' },
      options: ['default', 'custom', 'artifact-replacement'],
      description: 'Uses the default envelope, a tool-specific snippet, or an app-owned inspector.',
    },
    scenario: {
      control: { type: 'inline-radio' },
      options: ['complete', 'executing', 'error', 'json-result', 'edit-file'],
      description: 'Changes the projected tool-call state.',
    },
  },
} satisfies Meta<ToolCallDemo>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Tutorial: Story = {
  args: {
    renderMode: 'custom',
    scenario: 'complete',
  },
};

export const DefaultEnvelope: Story = {
  args: {
    ...Tutorial.args,
    renderMode: 'default',
  },
};

export const Executing: Story = {
  args: {
    ...Tutorial.args,
    scenario: 'executing',
  },
};

export const ArtifactReplacement: Story = {
  args: {
    renderMode: 'artifact-replacement',
    scenario: 'edit-file',
  },
};
