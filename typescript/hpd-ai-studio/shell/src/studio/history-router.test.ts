import { afterEach, describe, expect, it } from 'vitest';
import { studioPageId, studioSha256, type StudioVisiblePage } from '@hpd-research/hpd-studio-core';
import { StudioHistoryRouter } from './history-router.ts';

const zero = studioSha256('0'.repeat(64));
function page(id: string, segments: StudioVisiblePage['route']['segments']): StudioVisiblePage {
  return Object.freeze({ moduleId: 'base', pageId: studioPageId(id), version: 1, area: id === 'base.overview' ? 'overview' : 'data',
    navigationRole: 'areaLanding', route: { id: `${id}.route`, segments, query: [] }, initialResource: null, acceptedResources: [],
    observationMethodIds: [], resolverMethodIds: [], presentation: { pageId: id, pageVersion: 1, navigationRole: 'areaLanding' as const, workspace: 'landing' as const,
      sections: [{ sectionId: 'summary', labelMessageId: 'studio.section.summary', order: 0, kind: 'summary' as const, viewIds: [], commandIds: [], checksum: zero }],
      resourceRail: null, contextualDetail: null, draftRetention: 'none' as const, checksum: zero }, views: [], registrationChecksum: zero });
}

const original = { location: globalThis.location, history: globalThis.history, document: globalThis.document,
  add: globalThis.addEventListener, remove: globalThis.removeEventListener };
afterEach(() => { Object.assign(globalThis, { location: original.location, history: original.history, document: original.document,
  addEventListener: original.add, removeEventListener: original.remove }); });

describe('Studio History router', () => {
  it('matches the canonical root and uses pushState without hashes', () => {
    const pushed: URL[] = [];
    Object.assign(globalThis, { document: { baseURI: 'https://studio.test/studio/' }, location: { pathname: '/studio/', search: '' },
      history: { pushState: (_: unknown, __: string, value: URL) => pushed.push(value), replaceState() {} },
      addEventListener() {}, removeEventListener() {} });
    const router = new StudioHistoryRouter([page('base.overview', []), page('base.data', [{ kind: 'literal', value: 'data' }])]);
    expect(router.current?.page.pageId).toBe('base.overview'); router.navigate('/data');
    expect(pushed[0]?.pathname).toBe('/studio/data'); expect(pushed[0]?.hash).toBe(''); router.dispose();
  });
  it('rejects unknown and malformed paths', () => {
    Object.assign(globalThis, { document: { baseURI: 'https://studio.test/studio/' }, location: { pathname: '/studio/nope', search: '' },
      history: { pushState() {}, replaceState() {} }, addEventListener() {}, removeEventListener() {} });
    const router = new StudioHistoryRouter([page('base.data', [{ kind: 'literal', value: 'data' }])]);
    expect(router.current).toBeNull(); expect(router.navigate('/data/')).toBeNull(); router.dispose();
  });
  it('isolates hostile route observers', () => {
    Object.assign(globalThis, { document: { baseURI: 'https://studio.test/studio/' }, location: { pathname: '/studio/data', search: '' },
      history: { pushState() {}, replaceState() {} }, addEventListener() {}, removeEventListener() {} });
    const router = new StudioHistoryRouter([page('base.data', [{ kind: 'literal', value: 'data' }])]); let observed = 0;
    router.subscribe(() => { throw new Error('hostile'); }); router.subscribe(() => observed++); router.navigate('/data');
    expect(observed).toBe(2); router.dispose();
  });
});
