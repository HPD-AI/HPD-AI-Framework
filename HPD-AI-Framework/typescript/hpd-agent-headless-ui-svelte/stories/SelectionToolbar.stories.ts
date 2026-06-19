import type { Meta, StoryObj } from '@storybook/svelte-vite';
import SelectionToolbarDemo from './SelectionToolbarDemo.svelte';

const meta = {
  title: 'Thread/SelectionToolbar',
  component: SelectionToolbarDemo,
  tags: ['autodocs'],
  argTypes: {
    placement: {
      control: { type: 'inline-radio' },
      options: ['above', 'below'],
      description: 'Places the floating toolbar above or below the selected range.',
    },
  },
} satisfies Meta<SelectionToolbarDemo>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {
  args: {
    placement: 'above',
  },
};

export const Below: Story = {
  args: {
    ...Default.args,
    placement: 'below',
  },
};
