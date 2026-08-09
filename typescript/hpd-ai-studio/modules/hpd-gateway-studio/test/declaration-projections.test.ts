import { describe, expect, it } from 'vitest';
import { parseGatewayJson } from '../src/authored-json.ts';
import { diffGatewayDocuments, projectGatewayNavigator, searchGatewayNavigator } from '../src/declaration-projections.ts';

describe('Gateway declaration projections',()=>{
  it('projects stable resources, references, unresolved and orphan truth',()=>{
    const graph=parse('{"schemaVersion":"1.0","canonicalizationVersion":1,"routes":[{"id":{"value":"route-a"},"upstreamRef":{"value":"up-a"},"listenerRef":{"value":"https"}}],"upstreams":[{"id":{"value":"up-a"},"endpointSource":{"kind":"Static","destinations":[{"id":{"value":"dest-a"},"address":"https://example.test"}]}},{"id":{"value":"orphan"},"endpointSource":{"kind":"Static","destinations":[]}}]}');
    const projection=projectGatewayNavigator(graph);
    expect(projection.entries.map(value=>value.key)).toEqual(['destination:up-a:dest-a','listener:https','route:route-a','upstream:orphan','upstream:up-a']);
    expect(projection.entries.find(value=>value.key==='upstream:up-a')?.usedBy).toEqual(['route:route-a']);
    expect(projection.entries.find(value=>value.key==='upstream:orphan')?.orphan).toBe(true);
    expect(projection.entries.find(value=>value.key==='route:route-a')?.unresolved).toBe(false);
  });
  it('uses bounded ordinal local search only',()=>{const projection=projectGatewayNavigator(parse('{"schemaVersion":"1.0","canonicalizationVersion":1,"routes":[{"id":{"value":"route-a"}}]}'));expect(searchGatewayNavigator(projection,'route-a')).toHaveLength(1);expect(searchGatewayNavigator(projection,'remote')).toHaveLength(0);});
  it('computes deterministic semantic differences without treating number lexemes as changes',()=>{const baseline=parse('{"schemaVersion":"1.0","canonicalizationVersion":1,"routes":[]}');const equal=parse('{"canonicalizationVersion":1e0,"schemaVersion":"1.0","routes":[]}');expect(diffGatewayDocuments(equal,baseline).differences).toEqual([]);const changed=parse('{"schemaVersion":"1.0","canonicalizationVersion":1,"routes":[{"id":{"value":"a"}}]}');expect(diffGatewayDocuments(changed,baseline).differences).toEqual([{pointer:'/routes/0',kind:'added'}]);});
});
function parse(value:string){const result=parseGatewayJson(value);if(!result.ok)throw new Error(result.diagnostic.code);return result.graph;}
