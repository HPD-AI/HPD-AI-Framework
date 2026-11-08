import assert from 'node:assert/strict';
import fs from 'node:fs';
import {
  ClientToolRegistry,
  HpdJournalRebasedError,
  HpdSseState,
  HpdStreamError,
  clientToolText,
} from './dist/index.js';

const document = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));
for (const fixture of document.fixtures) {
  if (fixture.kind === 'sse-state') {
    const state = new HpdSseState(fixture.initialCursor);
    if (fixture.expectedError) {
      let error;
      try {
        for (const item of fixture.messages) state.apply(item, item.sequence ?? undefined);
      } catch (caught) {
        error = caught;
      }
      assert.ok(error);
      if (fixture.expectedError === 'journal-rebased') assert.ok(error instanceof HpdJournalRebasedError);
      else assert.ok(error instanceof HpdStreamError && error.message.includes('did not match'));
    } else {
      assert.deepEqual(
        fixture.messages.map(item => state.apply(item, item.sequence ?? undefined)),
        fixture.expectedDecisions,
      );
    }
    if (fixture.expectedCursor) assert.deepEqual(state.cursor, fixture.expectedCursor);
  } else if (fixture.kind === 'json-shape') {
    const actual = fixture.operation === 'run-config-empty'
      ? {}
      : {
          role: 'user',
          contents: [{ $type: 'text', text: fixture.input }],
        };
    assert.deepEqual(actual, fixture.expected);
  } else if (fixture.kind === 'client-tool') {
    const registry = new ClientToolRegistry();
    if (fixture.registration) registry.register(fixture.registration, () => clientToolText(fixture.handler));
    const result = await registry.invoke(fixture.invocation);
    assert.equal(result.outcome, fixture.expectedOutcome);
    if (fixture.expectedCapability) assert.ok(registry.capabilities.includes(fixture.expectedCapability));
    if (fixture.expectedContent) assert.deepEqual(result.content, [fixture.expectedContent]);
    if (fixture.expectedErrorMessageContains) assert.ok(result.errorMessage?.includes(fixture.expectedErrorMessageContains));
  }
}
console.log(`TypeScript conformance passed: ${document.fixtures.length} fixtures`);