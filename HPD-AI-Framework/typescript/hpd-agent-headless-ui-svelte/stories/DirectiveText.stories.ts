import type { Meta, StoryObj } from '@storybook/svelte-vite';
import DirectiveTextDemo from './DirectiveTextDemo.svelte';

const meta = {
  title: 'Thread/DirectiveText',
  component: DirectiveTextDemo,
  tags: ['autodocs'],
  argTypes: {
    variant: {
      control: { type: 'inline-radio' },
      options: ['default', 'custom', 'message-parts'],
      description: 'Chooses default rendering, custom chip snippet, or MessageParts integration.',
    },
  },
} satisfies Meta<DirectiveTextDemo>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {
  args: {
    variant: 'default',
  },
};

export const Custom: Story = {
  args: {
    variant: 'custom',
  },
};

export const MessagePartsIntegration: Story = {
  args: {
    variant: 'message-parts',
  },
};
