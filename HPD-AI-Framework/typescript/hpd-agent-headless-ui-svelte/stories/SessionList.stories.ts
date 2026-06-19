import type { Meta, StoryObj } from '@storybook/svelte-vite';
import SessionListDemo from './SessionListDemo.svelte';

const meta = {
  title: 'Session/SessionListPrimitives',
  component: SessionListDemo,
  tags: ['autodocs'],
  argTypes: {
    renderMode: {
      control: { type: 'inline-radio' },
      options: ['default', 'item', 'empty', 'error'],
      description: 'Uses default rows, a custom item snippet, or empty/error states.',
    },
  },
} satisfies Meta<SessionListDemo>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Tutorial: Story = {
  args: {
    renderMode: 'default',
  },
};

export const CustomItem: Story = {
  args: {
    renderMode: 'item',
  },
};

export const Empty: Story = {
  args: {
    renderMode: 'empty',
  },
};

export const Error: Story = {
  args: {
    renderMode: 'error',
  },
};
