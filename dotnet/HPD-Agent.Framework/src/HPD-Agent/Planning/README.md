# Planning

Enable persistent plan tracking with `.WithPlanMode()` on `AgentBuilder`. The model sees one synchronous `plan` tool. Its `operation` argument is a discriminated action object:

```json
{"operation":{"action":"create","goal":"Verify the migration","steps":["Inspect","Test"]}}
```

| Action | Fields |
| --- | --- |
| `create` | `goal`, `steps` |
| `updateStep` | `stepId`, `status`, optional `notes` |
| `addStep` | `description`, optional `afterStepId` |
| `addNote` | `note` |
| `complete` | None |

Step statuses in the generated schema are `Pending`, `InProgress`, `Completed`, and `Blocked`. For example:

```json
{"operation":{"action":"updateStep","stepId":"1","status":"Completed","notes":"Verified"}}
```

The current plan is injected into context. Creating another plan is rejected while the existing one is unfinished; completion is rejected until all steps are completed. Completing a plan does not automatically complete a persistent Goal.

The five former tool names (`create_plan`, `update_plan_step`, `add_plan_step`, `add_context_note`, `complete_plan`) have been replaced, with no aliases. Consumers identifying tool calls should match the `AgentPlanToolHarness` harness and `plan` function. Event consumers continue using the same durable `PlanUpdatedEvent`; plan state storage and recovery are unchanged by the tool consolidation.

HPDOS shows an event-driven checklist above the editor, with keyboard focus, expandable notes, and bounded scrolling.
