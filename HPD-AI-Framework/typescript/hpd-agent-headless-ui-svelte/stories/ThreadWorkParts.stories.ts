import type { Meta, StoryObj } from '@storybook/svelte-vite';
import ThreadWorkPartsDemo from './ThreadWorkPartsDemo.svelte';

const meta = {
  title: 'Thread/ThreadWorkParts',
  component: ThreadWorkPartsDemo,
  tags: ['autodocs'],
  argTypes: {
    renderMode: {
      control: { type: 'inline-radio' },
      options: ['default', 'custom'],
      description: 'Uses package defaults or a custom workPart snippet.',
    },
    showFinalDraft: {
      control: 'boolean',
      description: 'Keeps promoted final assistant drafts visible inside work parts.',
    },
  },
} satisfies Meta<ThreadWorkPartsDemo>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Tutorial: Story = {
  args: {
    renderMode: 'custom',
    showFinalDraft: false,
  },
};

export const DefaultParts: Story = {
  args: {
    ...Tutorial.args,
    renderMode: 'default',
  },
};
