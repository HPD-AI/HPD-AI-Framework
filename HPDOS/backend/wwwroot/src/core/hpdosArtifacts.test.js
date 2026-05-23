import { describe, expect, test } from "bun:test";
import { HpdosArtifacts, isArtifactToolName } from "./hpdosArtifacts.ts";

function invoke(artifacts, toolName, args) {
  const response = artifacts.handleToolRequest({
    type: "CLIENT_TOOL_INVOKE_REQUEST",
    requestId: `request-${toolName}`,
    toolName,
    callId: `call-${toolName}`,
    arguments: args
  });

  if (!response) throw new Error(`Tool was not handled: ${toolName}`);
  return response;
}

function jsonValue(response) {
  const content = response.content[0];
  if (content?.type !== "json") throw new Error("Expected JSON tool result.");
  return content.value;
}

function artifactContent(response) {
  return jsonValue(response).artifact?.content;
}

describe("HpdosArtifacts", () => {
  test("publishes the clean read/write/edit tool surface only", () => {
    const artifacts = new HpdosArtifacts();
    const toolNames = artifacts.harness.tools.map((tool) => tool.name);

    expect(toolNames).toEqual([
      "list_artifacts",
      "read_artifact",
      "write_artifact",
      "edit_artifact",
      "open_artifact",
      "close_artifact"
    ]);
    expect(toolNames).not.toContain("create_artifact");
    expect(toolNames).not.toContain("update_artifact");
    expect(isArtifactToolName("create_artifact")).toBe(false);
    expect(isArtifactToolName("write_artifact")).toBe(true);
  });

  test("write_artifact creates an artifact and read_artifact returns exact content", () => {
    const artifacts = new HpdosArtifacts();
    const content = "  <section>\n    <h1>The Chicken Lab</h1>\n  </section>\n";

    const write = invoke(artifacts, "write_artifact", {
      id: "chicken-science-pro-ui",
      title: "Scientific Chicken Dashboard (Caged)",
      type: "html",
      content
    });

    expect(write.success).toBe(true);
    expect(artifactContent(write)).toBe(content);
    expect(artifacts.openArtifactId).toBe("chicken-science-pro-ui");

    const read = invoke(artifacts, "read_artifact", { id: "chicken-science-pro-ui" });
    expect(read.success).toBe(true);
    expect(artifactContent(read)).toBe(content);
  });

  test("write_artifact allows empty content", () => {
    const artifacts = new HpdosArtifacts();
    const response = invoke(artifacts, "write_artifact", {
      id: "empty",
      title: "Empty",
      type: "text",
      content: ""
    });

    expect(response.success).toBe(true);
    expect(artifactContent(response)).toBe("");
  });

  test("edit_artifact supports exact deletion with an empty newString", () => {
    const artifacts = new HpdosArtifacts();
    invoke(artifacts, "write_artifact", {
      id: "cage",
      title: "Cage",
      type: "text",
      content: "alpha\ncaged\nomega\n"
    });

    const edit = invoke(artifacts, "edit_artifact", {
      id: "cage",
      oldString: "caged\n",
      newString: ""
    });

    expect(edit.success).toBe(true);
    expect(artifactContent(edit)).toBe("alpha\nomega\n");
  });

  test("edit_artifact rejects ambiguous matches unless replaceAll is true", () => {
    const artifacts = new HpdosArtifacts();
    invoke(artifacts, "write_artifact", {
      id: "ambiguous",
      title: "Ambiguous",
      type: "text",
      content: "cage cage"
    });

    const ambiguous = invoke(artifacts, "edit_artifact", {
      id: "ambiguous",
      oldString: "cage",
      newString: "coop"
    });
    expect(ambiguous.success).toBe(false);
    expect(ambiguous.errorMessage).toContain("Ambiguous match");

    const replaceAll = invoke(artifacts, "edit_artifact", {
      id: "ambiguous",
      oldString: "cage",
      newString: "coop",
      replaceAll: true
    });
    expect(replaceAll.success).toBe(true);
    expect(artifactContent(replaceAll)).toBe("coop coop");
  });

  test("edit_artifact reports clean no-match and no-change errors", () => {
    const artifacts = new HpdosArtifacts();
    invoke(artifacts, "write_artifact", {
      id: "errors",
      title: "Errors",
      type: "text",
      content: "one two"
    });

    const missing = invoke(artifacts, "edit_artifact", {
      id: "errors",
      oldString: "three",
      newString: "four"
    });
    expect(missing.success).toBe(false);
    expect(missing.errorMessage).toContain("No match");

    const noChange = invoke(artifacts, "edit_artifact", {
      id: "errors",
      oldString: "one",
      newString: "one"
    });
    expect(noChange.success).toBe(false);
    expect(noChange.errorMessage).toContain("No change");
  });

  test("history tool calls reconstruct artifact state", () => {
    const artifacts = new HpdosArtifacts();

    expect(artifacts.applyHistoryToolCall("write_artifact", {
      id: "history-artifact",
      title: "History",
      type: "text",
      content: "alpha beta"
    }, "2026-01-01T00:00:00Z")).toBe(true);
    expect(artifacts.applyHistoryToolCall("edit_artifact", {
      id: "history-artifact",
      oldString: "beta",
      newString: "gamma"
    }, "2026-01-01T00:00:01Z")).toBe(true);

    expect(artifacts.all).toEqual([
      expect.objectContaining({
        id: "history-artifact",
        content: "alpha gamma",
        createdAt: "2026-01-01T00:00:00Z",
        updatedAt: "2026-01-01T00:00:01Z"
      })
    ]);
    expect(artifacts.openArtifactId).toBe("history-artifact");
  });
});
