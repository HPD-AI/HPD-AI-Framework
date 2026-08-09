import { createHash } from 'node:crypto';
import { describe, expect, it } from 'vitest';
import { gatewayJsonSemanticEqual, parseGatewayJson, serializeGatewayJson } from '../src/authored-json.ts';
import { createGatewayDeclarationController, initialGatewayDocument } from '../src/declaration-state.ts';

describe('lossless authored Gateway document', () => {
  it('starts at the exact generation-zero document and framed source identity', () => {
    const controller = createGatewayDeclarationController();
    const document = controller.snapshot().document;
    expect(document.utf8Text).toBe('{"schemaVersion":{"major":1,"minor":0},"canonicalizationVersion":1}');
    expect(document.editGeneration).toBe(0n);
    expect(document.state).toBe('LocallyValidNotServerValidated');
    expect(document.sourceSha256).toBe(framed(initialGatewayDocument));
  });

  it('retains exact number lexemes and semantic numeric equality', () => {
    const source = '{"schemaVersion":"1.0","canonicalizationVersion":1,"routes":[],"upstreams":[],"definitions":{"authorization":[],"cors":[],"trafficAdmission":[],"requestTimeout":[],"outputCache":[],"telemetry":[],"inspection":[],"credentialDisposition":[],"requestTransform":[],"responseTransform":[]},"metadata":[],"root":{"requestTimeout":{"value":-0e9}}}';
    const parsed = parseGatewayJson(source); expect(parsed.ok).toBe(true);
    if (!parsed.ok) return;
    expect(serializeGatewayJson(parsed.graph)).toContain('-0e9');
    const canonical = parseGatewayJson(serializeGatewayJson(parsed.graph));
    expect(canonical.ok && gatewayJsonSemanticEqual(parsed.graph, canonical.graph)).toBe(true);
  });

  it('rejects decoded duplicate names, malformed Unicode, and unsafe bounds without throwing', () => {
    expect(parseGatewayJson('{"name":1,"\\u006eame":2}')).toMatchObject({ ok: false, diagnostic: { code: 'duplicate-property' } });
    expect(parseGatewayJson('{"x":"\\ud800"}')).toMatchObject({ ok: false });
    expect(parseGatewayJson(`[${Array.from({length:10_001},()=>0).join(',')}]`)).toMatchObject({ ok: false });
    const properties = Array.from({ length: 257 }, (_, index) => `"p${index}":0`).join(',');
    expect(parseGatewayJson(`{${properties}}`)).toMatchObject({ ok: false, diagnostic: { code: 'property-bound-exceeded' } });
  });

  it('reports bounded diagnostics and the truthful number truncated',()=>{const controller=createGatewayDeclarationController();const routes=Array.from({length:300},(_,index)=>`{"invented${index}":true}`).join(',');const document=controller.replaceRaw(`{"schemaVersion":{"major":1,"minor":0},"canonicalizationVersion":1,"routes":[${routes}]}`);expect(document.state).toBe('RawOnlyIncompatible');expect(document.diagnostics).toHaveLength(1024);expect(document.truncatedDiagnostics).toBeGreaterThan(0);expect(document.diagnostics.length+document.truncatedDiagnostics).toBe(1200);});

  it('advances one authority generation for malformed and incompatible raw text', () => {
    const controller = createGatewayDeclarationController();
    const malformed = controller.replaceRaw('{');
    expect(malformed.editGeneration).toBe(1n); expect(malformed.state).toBe('RawMalformed');
    expect(malformed.compatibleGraph).toBeNull();
    const incompatible = controller.replaceRaw('{"schemaVersion":"1.0","canonicalizationVersion":1,"invented":true}');
    expect(incompatible.editGeneration).toBe(2n); expect(incompatible.state).toBe('RawOnlyIncompatible');
    expect(incompatible.compatibleGraph).toBeNull();
    expect(controller.snapshot().lastCompatibleHistory?.editGeneration).toBe(0n);
  });

  it('selects and enforces the exact endpoint-source discriminator branch', () => {
    const controller=createGatewayDeclarationController();
    const prefix='{"schemaVersion":{"major":1,"minor":0},"canonicalizationVersion":1,"upstreams":[{"id":{"value":"up"},"endpoints":';
    expect(controller.replaceRaw(prefix+'{"kind":"static","destinations":[]}}]}').state).toBe('LocallyValidNotServerValidated');
    expect(controller.replaceRaw(prefix+'{"kind":"discovery","provider":{"value":"dns"},"service":{"value":"svc"},"staleBehavior":"RejectActivationUntilFresh"}}]}').state).toBe('LocallyValidNotServerValidated');
    const mixed=controller.replaceRaw(prefix+'{"kind":"static","destinations":[],"provider":{"value":"dns"}}}]}');
    expect(mixed.state).toBe('RawOnlyIncompatible');
    expect(mixed.diagnostics.some(value=>value.code==='unknown-property'&&value.path.endsWith('/provider'))).toBe(true);
    const missing=controller.replaceRaw(prefix+'{"destinations":[]}}]}');
    expect(missing.state).toBe('RawOnlyIncompatible');
    expect(missing.diagnostics.some(value=>value.code==='union-discriminator-unknown')).toBe(true);
  });

  it('applies visual changes only to the current compatible document and preserves an explicit baseline', () => {
    const controller = createGatewayDeclarationController();
    expect(controller.captureBaseline(new Date('2026-01-01T00:00:00Z'))).toBe(true);
    expect(controller.setAtPointer('/canonicalizationVersion', Object.freeze({ kind: 'number', lexeme: '01' }))).toBe(false);
    expect(controller.setAtPointer('/canonicalizationVersion', Object.freeze({ kind: 'number', lexeme: '1' }))).toBe(true);
    expect(controller.snapshot().document.editGeneration).toBe(1n);
    expect(controller.snapshot().baseline?.editGeneration).toBe(0n);
    controller.replaceRaw('{');
    expect(controller.setAtPointer('/canonicalizationVersion', Object.freeze({ kind: 'number', lexeme: '1' }))).toBe(false);
  });

  it('clears all retained candidate history on principal replacement and disposal', () => {
    const controller = createGatewayDeclarationController(); controller.captureBaseline(); controller.replaceRaw('{');
    controller.clearPrincipal();
    expect(controller.snapshot().document.utf8Text).toBe(initialGatewayDocument);
    expect(controller.snapshot().baseline).toBeNull(); expect(controller.snapshot().lastCompatibleHistory).toBeNull();
    controller.dispose();
  });

  it('correlates authoritative validation to the exact generation and host snapshot', async () => {
    const calls: unknown[]=[];
    const client={validate:async(input:unknown)=>{calls.push(input);return{ok:true,status:200,value:{canonicalizationVersion:'1',contentHashAlgorithm:'sha-256',contentHashValue:'a'.repeat(64),correlationId:'correlation',diagnostics:[],hostCapabilitySnapshotAlgorithm:'sha-256',hostCapabilitySnapshotValue:'b'.repeat(64),isValid:true,observedAt:'2026-01-01T00:00:00Z',schemaVersion:'1.0'},headers:{}};}} as any;
    const controller=createGatewayDeclarationController({client,hostCapabilityIdentity:()=>({algorithm:'sha-256',value:'b'.repeat(64)})});
    expect(await controller.validate()).toBe(true); expect(calls).toHaveLength(1);
    expect(controller.snapshot().document.state).toBe('ServerValid');
    expect(controller.snapshot().document.validation?.editGeneration).toBe(0n);
    expect(controller.snapshot().document.validation?.validationTransportSha256).toMatch(/^[0-9a-f]{64}$/u);
    controller.invalidateHostCapabilities(); expect(controller.snapshot().document.state).toBe('ServerValidationStale');
  });

  it('rejects stale validation after a concurrent edit or capability change', async () => {
    let release!:()=>void; const wait=new Promise<void>(resolve=>{release=resolve;});
    const client={validate:async()=>{await wait;return{ok:true,status:200,value:{canonicalizationVersion:'1',contentHashAlgorithm:'sha-256',contentHashValue:'a'.repeat(64),correlationId:'c',diagnostics:[],hostCapabilitySnapshotAlgorithm:'sha-256',hostCapabilitySnapshotValue:'b'.repeat(64),isValid:true,observedAt:'2026-01-01T00:00:00Z',schemaVersion:'1.0'},headers:{}};}} as any;
    let capability='b'.repeat(64); const controller=createGatewayDeclarationController({client,hostCapabilityIdentity:()=>({algorithm:'sha-256',value:capability})});
    const pending=controller.validate(); controller.replaceRaw(initialGatewayDocument+' '); release();
    expect(await pending).toBe(false); expect(controller.snapshot().document.validation).toBeNull();
    capability='c'.repeat(64); expect(await controller.validate()).toBe(false);
  });
});

function framed(value: string): string {
  const bytes = new TextEncoder().encode(value); const size = Buffer.alloc(8); size.writeBigUInt64BE(BigInt(bytes.byteLength));
  return createHash('sha256').update('hpd.gateway.authored-source.v1\0').update(size).update(bytes).digest('hex');
}
