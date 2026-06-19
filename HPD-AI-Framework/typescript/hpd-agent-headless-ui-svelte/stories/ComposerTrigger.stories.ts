import type { Meta, StoryObj } from '@storybook/svelte-vite';
import ComposerTriggerDemo from './ComposerTriggerDemo.svelte';

const meta = {
  title: 'Thread/ComposerTrigger',
  component: ComposerTriggerDemo,
  tags: ['autodocs'],
  argTypes: {
    initialValue: {
      control: 'text',
      description: 'Initial bound composer value.',
    },
    renderMode: {
      control: { type: 'inline-radio' },
      options: ['default', 'custom'],
      description: 'Uses default trigger items or a custom item snippet.',
    },
  },
} satisfies Meta<ComposerTriggerDemo>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {
  args: {
    initialValue: 'Ask @wor about /deep',
    renderMode: 'default',
  },
};

export const CustomItems: Story = {
  args: {
    ...Default.args,
    renderMode: 'custom',
  },
};

