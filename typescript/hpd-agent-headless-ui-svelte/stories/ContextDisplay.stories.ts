import type { Meta, StoryObj } from '@storybook/svelte-vite';
import ContextDisplayDemo from './ContextDisplayDemo.svelte';

const meta = {
  title: 'Thread/ContextDisplay',
  component: ContextDisplayDemo,
  tags: ['autodocs'],
  argTypes: {
    scenario: {
      control: { type: 'inline-radio' },
      options: ['normal', 'warning', 'critical'],
      description: 'Chooses usage relative to the model context window.',
    },
    variant: {
      control: { type: 'inline-radio' },
      options: ['bar', 'ring', 'text', 'custom'],
      description: 'Chooses the visual primitive or a custom snippet.',
    },
  },
} satisfies Meta<ContextDisplayDemo>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Bar: Story = {
  args: {
    scenario: 'normal',
    variant: 'bar',
  },
};

export const Ring: Story = {
  args: {
    ...Bar.args,
    variant: 'ring',
  },
};

export const Critical: Story = {
  args: {
    ...Bar.args,
    scenario: 'critical',
  },
};

export const Custom: Story = {
  args: {
    ...Bar.args,
    variant: 'custom',
  },
};

