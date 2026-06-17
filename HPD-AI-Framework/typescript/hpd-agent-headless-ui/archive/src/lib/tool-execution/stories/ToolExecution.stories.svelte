<script context="module">
	import { defineMeta } from '@storybook/addon-svelte-csf';
	import ToolExecutionDemo from './ToolExecutionDemo.svelte';

	const { Story } = defineMeta({
		title: 'Components/ToolExecution',
		component: ToolExecutionDemo,
		tags: ['autodocs'],
		argTypes: {
			name: {
				control: 'text',
				description: 'Tool name'
			},
			status: {
				control: { type: 'select' },
				options: ['pending', 'executing', 'complete', 'error'],
				description: 'Tool execution status'
			},
			args: {
				control: 'object',
				description: 'Tool arguments (JSON object)'
			},
			result: {
				control: 'text',
				description: 'Tool execution result'
			},
			error: {
				control: 'text',
				description: 'Tool execution error message'
			},
			expanded: {
				control: 'boolean',
				description: 'Whether the tool details are expanded'
			},
			showExpandToggle: {
				control: 'boolean',
				description: 'Show expand toggle control (demo only)'
			},
			showEventLog: {
				control: 'boolean',
				description: 'Show event log (demo only)'
			},
			interactive: {
				control: 'boolean',
				description: 'Show interactive status controls (demo only)'
			}
		}
	});
</script>

<!-- Default Story - Pending State -->
<Story
	name="Default (Pending)"
	args={{
		name: 'fetchWeather',
		status: 'pending',
		args: { city: 'San Francisco', units: 'metric' },
		result: undefined,
		error: undefined,
		expanded: false,
		showExpandToggle: true,
		showEventLog: true,
		interactive: true
	}}
/>

<!-- Executing State -->
<Story
	name="Executing"
	args={{
		name: 'searchDatabase',
		status: 'executing',
		args: { query: 'user data', limit: 100, offset: 0 },
		result: undefined,
		error: undefined,
		expanded: true,
		showExpandToggle: true,
		showEventLog: true,
		interactive: true
	}}
/>

<!-- Complete with Result -->
<Story
	name="Complete with Result"
	args={{
		name: 'calculateSum',
		status: 'complete',
		args: { numbers: [1, 2, 3, 4, 5] },
		result: 'Sum calculated: 15',
		error: undefined,
		expanded: true,
		showExpandToggle: true,
		showEventLog: true,
		interactive: false
	}}
/>

<!-- Complete with JSON Result -->
<Story
	name="Complete with JSON Result"
	args={{
		name: 'getUserProfile',
		status: 'complete',
		args: { userId: '12345' },
		result: JSON.stringify(
			{
				id: '12345',
				name: 'John Doe',
				email: 'john@example.com',
				role: 'admin'
			},
			null,
			2
		),
		error: undefined,
		expanded: true,
		showExpandToggle: true,
		showEventLog: true,
		interactive: false
	}}
/>

<!-- Error State -->
<Story
	name="Error"
	args={{
		name: 'sendEmail',
		status: 'error',
		args: { to: 'user@example.com', subject: 'Hello', body: 'Test message' },
		result: undefined,
		error: 'Failed to send email: SMTP server connection timeout',
		expanded: true,
		showExpandToggle: true,
		showEventLog: true,
		interactive: false
	}}
/>

<!-- No Arguments -->
<Story
	name="No Arguments"
	args={{
		name: 'getCurrentTime',
		status: 'complete',
		args: {},
		result: '2024-01-15T10:30:00Z',
		error: undefined,
		expanded: true,
		showExpandToggle: false,
		showEventLog: false,
		interactive: false
	}}
/>

<!-- Complex Arguments -->
<Story
	name="Complex Arguments"
	args={{
		name: 'processOrder',
		status: 'executing',
		args: {
			orderId: 'ORD-2024-001',
			items: [
				{ id: 'item1', quantity: 2, price: 19.99 },
				{ id: 'item2', quantity: 1, price: 49.99 }
			],
			shipping: {
				address: '123 Main St',
				city: 'San Francisco',
				zipCode: '94102'
			},
			payment: {
				method: 'credit_card',
				last4: '4242'
			}
		},
		result: undefined,
		error: undefined,
		expanded: true,
		showExpandToggle: false,
		showEventLog: false,
		interactive: false
	}}
/>

<!-- Collapsed State -->
<Story
	name="Collapsed"
	args={{
		name: 'analyzeData',
		status: 'complete',
		args: { datasetId: 'ds-2024-001', includeStats: true },
		result: 'Analysis complete: 1,250 records processed',
		error: undefined,
		expanded: false,
		showExpandToggle: true,
		showEventLog: false,
		interactive: false
	}}
/>

<!-- Long Result -->
<Story
	name="Long Result"
	args={{
		name: 'generateReport',
		status: 'complete',
		args: { reportType: 'monthly', month: 'December', year: 2024 },
		result: `Monthly Report - December 2024
========================================

Summary:
- Total Revenue: $125,430.00
- Total Expenses: $89,250.00
- Net Profit: $36,180.00

Top Performing Products:
1. Product A - $45,000
2. Product B - $32,500
3. Product C - $21,000

Customer Metrics:
- New Customers: 125
- Returning Customers: 450
- Customer Retention Rate: 78%

Key Insights:
- Revenue increased 15% compared to last month
- Expenses decreased 8% due to operational improvements
- Customer satisfaction scores improved to 4.6/5.0

Recommendations:
1. Continue focusing on Product A marketing
2. Expand inventory for Product B
3. Launch customer loyalty program in Q1 2025`,
		error: undefined,
		expanded: true,
		showExpandToggle: false,
		showEventLog: false,
		interactive: false
	}}
/>

<!-- Interactive Demo -->
<Story
	name="Interactive Demo"
	args={{
		name: 'interactiveTool',
		status: 'pending',
		args: { demo: true, interactive: true },
		result: undefined,
		error: undefined,
		expanded: false,
		showExpandToggle: true,
		showEventLog: true,
		interactive: true
	}}
/>

<!-- Minimal Display -->
<Story
	name="Minimal (No Controls)"
	args={{
		name: 'simpleQuery',
		status: 'complete',
		args: { q: 'search term' },
		result: 'Found 42 results',
		error: undefined,
		expanded: true,
		showExpandToggle: false,
		showEventLog: false,
		interactive: false
	}}
/>

<!-- Custom Rendering Examples -->
<Story
	name="Custom: Image Generator"
	args={{
		renderMode: 'custom',
		name: 'imageGenerator',
		status: 'complete',
		args: { prompt: 'A sunset over mountains', style: 'realistic', size: '1024x1024' },
		result: 'https://picsum.photos/400/300?random=1',
		showExpandToggle: false,
		showEventLog: false,
		interactive: false
	}}
/>

<Story
	name="Custom: Search Tool"
	args={{
		renderMode: 'simple',
		name: 'search',
		status: 'complete',
		args: { query: 'React hooks documentation', filters: ['official', 'latest'] },
		result:
			'Found 5 relevant documentation pages about React hooks including useState, useEffect, and custom hooks.',
		showExpandToggle: false,
		showEventLog: false,
		interactive: false
	}}
/>

<Story
	name="Custom: Calculator"
	args={{
		renderMode: 'simple',
		name: 'calculator',
		status: 'complete',
		args: { expression: '42 * 17' },
		result: '714',
		showExpandToggle: false,
		showEventLog: false,
		interactive: false
	}}
/>
