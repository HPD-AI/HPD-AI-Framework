import{describe,expect,it}from'vitest';import{createGatewayDeclarationController}from'../src/declaration-state.ts';import{applyGatewayStructureAction,projectGatewayStructureActions}from'../src/structural-editor.ts';import{projectGatewayVisualFields}from'../src/visual-fields.ts';
describe('schema-driven structural editor',()=>{
  it('reaches Route, Upstream, destination, definitions, bindings, and alternate union fields from generation zero',()=>{
    const controller=createGatewayDeclarationController();
    const apply=(label:string,pointer='')=>{const action=projectGatewayStructureActions(controller.snapshot().document.compatibleGraph!).find(value=>value.label===label&&value.pointer.startsWith(pointer));expect(action,`${label} ${pointer}`).toBeDefined();expect(applyGatewayStructureAction(controller,action!)).toBe(true);};
    apply('Add routes'); apply('Add route'); apply('Add declarations'); apply('Add inspection'); apply('Add inline'); apply('Add upstreams'); apply('Add upstream');
    expect(projectGatewayVisualFields(controller.snapshot().document.compatibleGraph!,null).some(value=>value.family==='discovery')).toBe(true);
    const actions=projectGatewayStructureActions(controller.snapshot().document.compatibleGraph!);const staticBranch=actions.find(value=>value.label.includes('endpoints: static')||value.label==='Use static branch');expect(staticBranch).toBeDefined();expect(applyGatewayStructureAction(controller,staticBranch!)).toBe(true);
    apply('Add destinations'); apply('Add destination'); apply('Add definitions'); apply('Add authorization','/definitions'); apply('Add authorization','/definitions/authorization');
    const fields=projectGatewayVisualFields(controller.snapshot().document.compatibleGraph!,null);
    expect(fields.some(value=>value.pointer.startsWith('/routes/0/'))).toBe(true);
    expect(fields.some(value=>value.pointer.startsWith('/routes/0/declarations/inspection/inline/'))).toBe(true);
    expect(fields.some(value=>value.pointer.includes('/upstreams/0/endpoints/destinations/0/'))).toBe(true);
    expect(fields.some(value=>value.pointer.startsWith('/definitions/authorization/0/'))).toBe(true);
    expect(fields.some(value=>value.family==='discovery')).toBe(false);
  });
  it('removes optional containers and collection items through bounded structural transactions',()=>{const controller=createGatewayDeclarationController();controller.replaceRaw('{"schemaVersion":{"major":1,"minor":0},"canonicalizationVersion":1,"routes":[{"id":{"value":"r"},"match":{},"upstream":{"value":"u"}}],"upstreams":[{"id":{"value":"u"},"endpoints":{"kind":"static","destinations":[{"id":{"value":"d"},"address":"https://x.test"}]}}]}');const action=projectGatewayStructureActions(controller.snapshot().document.compatibleGraph!).find(value=>value.kind==='remove-item'&&value.pointer==='/routes/0');expect(action).toBeDefined();expect(applyGatewayStructureAction(controller,action!)).toBe(true);expect(controller.snapshot().document.utf8Text).toContain('"routes":[]');});
});
