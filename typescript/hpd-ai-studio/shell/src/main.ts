import App from './App.svelte';
import { mount } from 'svelte';
import './styles.css';

const target = document.getElementById('app');

if (!target) {
  throw new Error('HPD AI Studio mount target was not found.');
}

const app = mount(App, {
  target
});

export default app;
