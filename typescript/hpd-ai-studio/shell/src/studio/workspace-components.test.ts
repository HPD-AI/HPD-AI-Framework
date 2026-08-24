import { render } from 'svelte/server';
import { describe, expect, it } from 'vitest';
import { nextStudioGridFocusIndex, StudioBoundedGrid, StudioCommandWorkbench, StudioObservationState,
  StudioResourceLinks, StudioResourceWorkspace } from '@hpd-research/hpd-studio-design';
import { StudioRegisteredWorkspace } from '@hpd-research/hpd-studio-design';

describe('shared Studio workspace accessibility', () => {
  it.each([
    { state: 'unobserved' } as const,
    { state: 'loading', hasPrevious: false } as const,
    { state: 'loading', hasPrevious: true } as const,
    { state: 'current' } as const,
    { state: 'stale', code: 'base.studio.stale' } as const,
    { state: 'unavailable', code: 'base.studio.unavailable' } as const,
    { state: 'denied', code: 'base.studio.denied' } as const,
    { state: 'unsupported', code: 'base.studio.unsupported' } as const,
    { state: 'failed', code: 'base.studio.failed' } as const,
  ])('renders the $state observation fixture without exposing its safe code', observation => {
    const body = render(StudioObservationState, { props: { title: 'Fixture', observation } }).body;
    expect(body).toContain('aria-label="Fixture"');
    if ('code' in observation) expect(body).not.toContain(observation.code);
  });

  it('renders each protected observation boundary with semantic status', () => {
    const unavailable = render(StudioObservationState, { props: { title: 'Records', observation: { state: 'unavailable', code: 'base.studio.unavailable' } } }).body;
    const failed = render(StudioObservationState, { props: { title: 'Records', observation: { state: 'failed', code: 'base.studio.timeout' } } }).body;
    expect(unavailable).toContain('role="status"'); expect(unavailable).toContain('Resource unavailable');
    expect(failed).toContain('role="alert"'); expect(failed).not.toContain('base.studio.timeout');
  });

  it('keeps bounded rows in an accessible table with an explicit disclosed count', () => {
    const body = render(StudioBoundedGrid, { props: { caption: 'Authorized records', columns: [
      { id: 'name', label: 'Name', width: 'standard' }, { id: 'status', label: 'Status', width: 'compact' }],
      rows: [{ id: 'one', label: 'One', cells: { name: 'One', status: 'Ready' } }], selectedId: 'one' } }).body;
    expect(body).toContain('<table'); expect(body).toContain('<caption'); expect(body).toContain('1 disclosed rows');
    expect(body).toContain('aria-selected="true"'); expect(body).toContain('scope="col"');
  });

  it('renders task workspace landmarks without manufacturing resource items', () => {
    const body = render(StudioResourceWorkspace, { props: { eyebrow: 'HPD BASE Studio', title: 'Collections', description: 'Authorized view',
      observation: { state: 'current' }, railItems: [], columns: [{ id: 'kind', label: 'Kind', width: 'standard' }], rows: [] } }).body;
    expect(body).toContain('<main'); expect(body).toContain('aria-label="Resources"'); expect(body).toContain('No resource rail for this view');
    expect(body).toContain('No disclosed items');
  });

  it('bounds keyboard focus at both ends of the finite result set', () => {
    expect(nextStudioGridFocusIndex(0, 3, 'ArrowUp')).toBe(0);
    expect(nextStudioGridFocusIndex(0, 3, 'ArrowDown')).toBe(1);
    expect(nextStudioGridFocusIndex(1, 3, 'Home')).toBe(0);
    expect(nextStudioGridFocusIndex(1, 3, 'End')).toBe(2);
    expect(nextStudioGridFocusIndex(2, 3, 'ArrowDown')).toBe(2);
    expect(nextStudioGridFocusIndex(0, 0, 'End')).toBe(0);
  });

  it('does not manufacture commands or related resources', () => {
    const commands = { open() {}, snapshot: () => ({ kind: 'closed' }), subscribe: () => () => {},
      preview: async () => {}, acknowledge() {}, execute: async () => {}, resolve: async () => {}, close() {} };
    const workbench = render(StudioCommandWorkbench, { props: { commandIds: [], target: null, commands } }).body;
    const links = render(StudioResourceLinks, { props: { links: [], onnavigate: () => {} } }).body;
    expect(workbench).toContain('No command is disclosed');
    expect(links).toContain('No disclosed links');
  });

  it('renders every registered section and view instead of a synthetic identity row', () => {
    const commands = { open() {}, snapshot: () => ({ kind: 'closed' }), subscribe: () => () => {},
      preview: async () => {}, acknowledge() {}, execute: async () => {}, resolve: async () => {}, close() {} };
    const page = { pageId: 'base.fixture', presentation: { workspace: 'detail', resourceRail: null, sections: [
      { sectionId: 'summary', labelMessageId: 'studio.section.summary', viewIds: ['fixture.summary'], commandIds: [] },
      { sectionId: 'history', labelMessageId: 'studio.section.history', viewIds: ['fixture.history'], commandIds: ['fixture.retry'] } ] },
      views: [ { viewId: 'fixture.summary', presentation: { grid: null, chart: null, emptyState: 'noItems' } },
        { viewId: 'fixture.history', presentation: { grid: { maximumRows: 16, columns: [{ stablePropertyOrEdgeId: 'status', labelMessageId: 'studio.column.status', initiallyVisible: true }] }, chart: null, emptyState: 'noItems' } } ] };
    const body = render(StudioRegisteredWorkspace, { props: { eyebrow: 'HPD BASE Studio', page, resource: null, observation: { state: 'current' },
      views: { 'fixture.summary': { name: 'Current' }, 'fixture.history': [{ status: 'Completed' }] }, links: [], commands, onnavigate: () => {} } }).body;
    expect(body).toContain('studio.section.summary'); expect(body).toContain('studio.section.history');
    expect(body).toContain('Current'); expect(body).toContain('Completed'); expect(body).toContain('Review fixture.retry');
    expect(body).not.toContain('Current authority');
  });

  it('does not infer grid columns from hostile response members', () => {
    const commands = { open() {}, snapshot: () => ({ kind: 'closed' }), subscribe: () => () => {}, preview: async () => {}, acknowledge() {}, execute: async () => {}, resolve: async () => {}, close() {} };
    const page = { pageId: 'base.fixture', presentation: { workspace: 'detail', resourceRail: null, sections: [
      { sectionId: 'summary', labelMessageId: 'summary', viewIds: ['fixture.grid'], commandIds: [] }] }, views: [
      { viewId: 'fixture.grid', presentation: { grid: { maximumRows: 16, columns: [] }, chart: null, emptyState: 'noItems' } }] };
    const body = render(StudioRegisteredWorkspace, { props: { eyebrow: 'Studio', page, resource: null, observation: { state: 'current' },
      views: { 'fixture.grid': [{ undisclosed: 'must-not-render' }] }, links: [], commands, onnavigate: () => {} } }).body;
    expect(body).not.toContain('undisclosed'); expect(body).not.toContain('must-not-render');
  });
});
