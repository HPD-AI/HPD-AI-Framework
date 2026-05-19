using System.Net;
using System.Text;
using System.Text.Json;
using HPDAgent.Graph.Abstractions.Config;
using HPDAgent.Graph.Abstractions.Discovery;
using HPDAgent.Graph.Abstractions.Storage;
using HPDAgent.Graph.Hosting.Data;
using HPDAgent.Graph.Hosting.Lifecycle;
using Microsoft.AspNetCore.Http;

internal static class UiFragments
{
    public static JsonSerializerOptions JsonOptions() => new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static IResult Html(string html) => Results.Content(html, "text/html; charset=utf-8");

    public static GraphConfig SampleGraph() => new()
    {
        GraphId = $"workflow-{DateTimeOffset.UtcNow:yyyyMMdd}",
        GraphVersion = "1.0.0",
        Name = "New Workflow",
        Description = "A runtime workflow graph.",
        EntryNodeId = "START",
        ExitNodeId = "END",
        MaxIterations = 10,
        Nodes = new Dictionary<string, NodeConfig>
        {
            ["work"] = new()
            {
                Id = "work",
                Name = "Work",
                Type = NodeKindConfig.Handler,
                HandlerName = "work",
                EnableCheckpointing = true,
                OutputPortCount = 1
            }
        },
        Edges =
        [
            new EdgeConfig { From = "START", To = "work" },
            new EdgeConfig { From = "work", To = "END" }
        ]
    };

    public static string ModeSwitcher(string active) => $$"""
        <nav class="grid grid-cols-2 rounded-hpd border border-hpd-line bg-hpd-soft p-1 shadow-sm" aria-label="Mode">
          <button class="nav hpd-button min-h-10 border text-sm {{(active == "chat" ? "border-hpd-line bg-white text-hpd-ink shadow-sm" : "border-transparent text-hpd-muted hover:bg-white")}} selected:text-hpd-blue"
            {{(active == "chat" ? "aria-current=\"page\"" : "")}}
            data-title="Chat"
            data-subtitle="Talk with HPD-OS."
            hx-get="/ui/chat"
            hx-target="#view"
            hx-swap="innerHTML"
            type="button"><span class="font-mono text-xs">C</span>Chat</button>
          <button class="nav hpd-button min-h-10 border text-sm {{(active == "workflows" ? "border-hpd-line bg-white text-hpd-ink shadow-sm" : "border-transparent text-hpd-muted hover:bg-white")}} selected:text-hpd-blue"
            {{(active == "workflows" ? "aria-current=\"page\"" : "")}}
            data-title="Workflows"
            data-subtitle="Build, run, schedule, and resume graph workflows."
            hx-get="/ui/workflows"
            hx-target="#view"
            hx-swap="innerHTML"
            type="button"><span class="font-mono text-xs">W</span>Workflows</button>
        </nav>
        """;

    public static string ChatView() => $$"""
        <section class="grid h-full min-h-0 grid-cols-[270px_minmax(0,1fr)] gap-3 max-md:grid-cols-1">
          <aside class="hpd-panel flex min-h-0 flex-col rounded-hpd">
            <div class="border-b border-hpd-line bg-white/80 p-3">
              {{ModeSwitcher("chat")}}
            </div>
            <div class="grid gap-2 border-b border-hpd-line p-3">
              <button class="hpd-button bg-hpd-ink text-white" id="newSession" type="button">New Session</button>
              <div class="flex items-center justify-between">
                <span class="text-[11px] font-black uppercase tracking-wide text-hpd-muted">Sessions</span>
                <span class="hpd-badge" id="sessionCount">0</span>
              </div>
            </div>
            <div class="min-h-0 flex-1 overflow-auto p-2" id="sessions"></div>
          </aside>
          <section class="hpd-panel grid min-h-0 grid-rows-[minmax(0,1fr)_auto] overflow-hidden rounded-hpd">
            <div class="min-h-0 overflow-auto bg-hpd-soft/70 p-4" id="chat">
              <div class="mx-auto grid max-w-4xl gap-4" id="chatStack"></div>
            </div>
            <form class="border-t border-hpd-line bg-white p-3" id="composer">
              <div class="rounded-hpd border border-hpd-line bg-white p-2 shadow-sm">
                <textarea class="min-h-14 max-h-48 w-full bg-transparent px-3 py-2 text-sm leading-6 outline-none placeholder:text-slate-400" id="text" placeholder="Ask HPD-OS to inspect, edit, run, or explain..." required></textarea>
                <div class="flex items-center justify-between gap-2 border-t border-hpd-line pt-2">
                  <details>
                    <summary class="cursor-pointer px-2 py-1 text-xs font-black text-hpd-muted">Runtime</summary>
                    <div class="mt-2 grid w-[min(520px,80vw)] grid-cols-2 gap-2 rounded-hpd border border-hpd-line bg-hpd-soft p-3 max-sm:grid-cols-1">
                      <input class="hpd-input" id="provider" value="openrouter">
                      <input class="hpd-input" id="model" value="google/gemini-3.1-flash-lite">
                    </div>
                  </details>
                  <button class="hpd-button bg-hpd-blue text-white" id="send" type="submit">Send -></button>
                </div>
              </div>
            </form>
          </section>
        </section>
        """;

    public static string WorkflowShell(IReadOnlyList<StoredGraphSummary> workflows, string? selectedGraphId, string editor, string side) => $$"""
        <section class="grid h-full min-h-0 grid-cols-[270px_minmax(0,1fr)] gap-3 max-md:grid-cols-1">
          <aside class="hpd-panel flex min-h-0 flex-col rounded-hpd">
            <div class="border-b border-hpd-line bg-white/80 p-3">
              {{ModeSwitcher("workflows")}}
            </div>
            <div class="grid gap-2 border-b border-hpd-line p-3">
              <input class="hpd-input" name="search" placeholder="Search workflows"
                hx-get="/ui/workflows/list"
                hx-trigger="input changed delay:200ms, search"
                hx-target="#workflowList"
                hx-sync="this:replace">
              <div class="grid grid-cols-2 gap-2">
                <button class="hpd-button bg-hpd-blue text-white" hx-get="/ui/workflows/new" hx-target="#workflowEditor" type="button">New</button>
                <button class="hpd-button border border-hpd-line bg-white text-hpd-muted" hx-get="/ui/workflows/list" hx-target="#workflowList" type="button">Refresh</button>
              </div>
            </div>
            <div class="min-h-0 flex-1 overflow-auto p-2" id="workflowList">{{RenderWorkflowList(workflows, selectedGraphId)}}</div>
          </aside>
          <section class="hpd-panel min-h-0 overflow-hidden rounded-hpd" id="workflowEditor">{{editor}}</section>
        </section>
        """;

    public static string WorkflowSelected(GraphConfig graph, string handlersHtml, string side) => $$"""
        {{RenderWorkflowEditor(graph, handlersHtml)}}
        """;

    public static string WorkflowSaved(GraphConfig graph, string handlersHtml, IReadOnlyList<StoredGraphSummary> workflows, string graphId, string side) => $$"""
        {{RenderWorkflowEditor(graph, handlersHtml)}}
        <template><div id="workflowList" hx-swap-oob="innerHTML">{{RenderWorkflowList(workflows, graphId)}}</div></template>
        <template><div id="toast" hx-swap-oob="innerHTML" class="fixed inset-x-4 bottom-4 z-50 mx-auto max-w-2xl rounded-hpd border border-hpd-line bg-white p-3 text-sm font-semibold shadow-hpd">Saved {{H(graphId)}}</div></template>
        """;

    public static string RenderWorkflowList(IReadOnlyList<StoredGraphSummary> workflows, string? selectedGraphId)
    {
        if (workflows.Count == 0)
            return """<div class="rounded-hpd border border-dashed border-hpd-line bg-white/70 p-3 text-sm text-hpd-muted">No workflows yet.</div>""";

        var sb = new StringBuilder();
        foreach (var item in workflows.OrderByDescending(w => w.UpdatedAt))
        {
            var active = item.GraphId == selectedGraphId
                ? "border-blue-200 bg-blue-50"
                : "border-transparent hover:border-hpd-line hover:bg-white";
            sb.Append($$"""
              <button class="mb-1 grid w-full rounded-hpd border px-3 py-2 text-left {{active}}"
                hx-get="/ui/workflows/{{U(item.GraphId)}}"
                hx-target="#workflowEditor"
                type="button">
                <span class="truncate text-sm font-black">{{H(item.Name)}}</span>
                <span class="mt-1 truncate text-xs font-semibold text-hpd-muted">{{H(item.GraphId)}}</span>
              </button>
              """);
        }

        return sb.ToString();
    }

    public static string RenderWorkflowEditor(GraphConfig graph, string handlersHtml = "")
    {
        var json = JsonSerializer.Serialize(graph, JsonOptions());
        return $$"""
          <form class="grid h-full min-h-0 grid-rows-[auto_minmax(0,1fr)] overflow-hidden"
            hx-post="/ui/workflows/save"
            hx-target="#workflowEditor"
            hx-sync="this:replace">
            <div class="flex items-center justify-between gap-2 border-b border-hpd-line p-3">
              <div class="min-w-0">
                <h3 class="truncate text-sm font-black">{{H(graph.Name)}}</h3>
                <p class="truncate text-xs font-semibold text-hpd-muted">{{H(graph.GraphId)}} / {{H(graph.GraphVersion)}}</p>
              </div>
              <div class="flex gap-2">
                <button class="hpd-button border border-hpd-line bg-white text-hpd-muted" data-format-graph type="button">Format</button>
                <button class="hpd-button bg-hpd-green text-white" type="submit">Save</button>
              </div>
            </div>
            <div class="grid min-h-0 grid-cols-[minmax(0,1fr)_minmax(360px,42%)] max-xl:grid-cols-1">
              <div class="min-h-0 overflow-auto p-3">
                <div class="mb-2 flex gap-2">
                  <button class="hpd-button border border-hpd-line bg-hpd-ink text-white" type="button">Graph</button>
                  <button class="hpd-button border border-hpd-line bg-white text-hpd-muted" type="button" data-show-handlers>Handlers</button>
                </div>
                <div class="graph-grid relative min-h-[520px] overflow-auto rounded-hpd border border-hpd-line" id="graphPreview"></div>
                <div class="mt-3 hidden grid gap-2" id="handlerList">{{handlersHtml}}</div>
              </div>
              <textarea class="min-h-0 border-l border-hpd-line bg-slate-950 p-4 font-mono text-xs leading-6 text-slate-100 outline-none max-xl:min-h-[420px]" id="graphJson" name="graphJson" spellcheck="false">{{H(json)}}</textarea>
            </div>
          </form>
          """;
    }

    public static string RenderEditorError(string json, string message) => $$"""
      <div class="grid h-full min-h-0 grid-rows-[auto_minmax(0,1fr)] overflow-hidden">
        <div class="border-b border-red-200 bg-red-50 p-3 text-sm font-bold text-red-800">{{H(message)}}</div>
        <form class="min-h-0" hx-post="/ui/workflows/save" hx-target="#workflowEditor">
          <textarea class="h-full min-h-0 w-full bg-slate-950 p-4 font-mono text-xs leading-6 text-slate-100 outline-none" id="graphJson" name="graphJson" spellcheck="false">{{H(json)}}</textarea>
        </form>
      </div>
      """;

    public static async Task<string> RenderWorkflowSideAsync(string? graphId, SchedulingManager schedulingManager, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(graphId))
            return RenderNotice("Save or select a workflow to run and schedule it.");

        var schedule = await schedulingManager.GetScheduleAsync(graphId, ct).ConfigureAwait(false);
        return $$"""
          <div class="grid grid-cols-2 border-b border-hpd-line">
            <button class="hpd-button rounded-none text-hpd-blue" type="button">Run</button>
            <button class="hpd-button rounded-none text-hpd-muted" type="button">Schedule</button>
          </div>
          <div class="min-h-0 overflow-auto p-3">
            <div id="runPanel">{{RenderRunPanel(graphId, "", "No execution yet.", null)}}</div>
            <div class="mt-4" id="schedulePanel">{{RenderSchedulePanel(graphId, schedule, null)}}</div>
          </div>
          """;
    }

    public static string RenderRunPanel(string graphId, string? executionId, string statusText, string? notice)
    {
        var id = executionId ?? "";
        var noticeHtml = string.IsNullOrWhiteSpace(notice) ? "" : $"""<div class="rounded-hpd border border-hpd-line bg-white p-2 text-xs font-semibold text-hpd-muted">{H(notice)}</div>""";
        var refresh = string.IsNullOrWhiteSpace(id) ? "" : $"""<button class="hpd-button border border-hpd-line bg-white text-hpd-muted" hx-get="/ui/workflows/{U(graphId)}/status/{U(id)}" hx-target="#runPanel" type="button">Refresh Status</button>""";
        var cancel = string.IsNullOrWhiteSpace(id) ? "" : $"""<button class="hpd-button bg-hpd-amber text-white" hx-post="/ui/workflows/{U(graphId)}/cancel/{U(id)}" hx-target="#runPanel" type="button">Cancel</button>""";
        return $$"""
          <div class="grid gap-3">
            {{noticeHtml}}
            <form class="grid gap-3" hx-post="/ui/workflows/{{U(graphId)}}/run" hx-target="#runPanel" hx-sync="this:drop">
              <input class="hpd-input" name="executionId" value="{{H(id)}}" placeholder="Execution ID optional">
              <textarea class="hpd-input min-h-24 py-2 font-mono" name="executionInput">{}</textarea>
              <div class="grid grid-cols-2 gap-2">
                <button class="hpd-button bg-hpd-blue text-white" type="submit">Run</button>
                {{cancel}}
              </div>
            </form>
            {{refresh}}
            <pre class="json-box max-h-56">{{H(statusText)}}</pre>
          </div>
          """;
    }

    public static string RenderSchedulePanel(string graphId, ScheduledGraphDto? schedule, string? notice)
    {
        var noticeHtml = string.IsNullOrWhiteSpace(notice) ? "" : $"""<div class="rounded-hpd border border-hpd-line bg-white p-2 text-xs font-semibold text-hpd-muted">{H(notice)}</div>""";
        return $$"""
          <form class="grid gap-3" hx-post="/ui/workflows/{{U(graphId)}}/schedule" hx-target="#schedulePanel">
            <div class="text-[11px] font-black uppercase tracking-wide text-hpd-muted">Schedule</div>
            {{noticeHtml}}
            <input class="hpd-input" name="cronExpression" value="{{H(schedule?.Schedule.CronExpression ?? "0 3 * * *")}}">
            <input class="hpd-input" name="timeZoneId" value="{{H(schedule?.Schedule.TimeZoneId ?? "UTC")}}">
            <select class="hpd-input" name="enabled">
              <option value="true" {{Selected(schedule?.Enabled != false)}}>Enabled</option>
              <option value="false" {{Selected(schedule?.Enabled == false)}}>Disabled</option>
            </select>
            <div class="grid grid-cols-2 gap-2">
              <button class="hpd-button bg-hpd-green text-white" type="submit">Save</button>
              <button class="hpd-button bg-hpd-red text-white" hx-delete="/ui/workflows/{{U(graphId)}}/schedule" hx-target="#schedulePanel" type="button">Delete</button>
            </div>
            <div class="rounded-hpd border border-hpd-line bg-white p-3 text-xs font-semibold text-hpd-muted">
              Next run: {{H(schedule?.NextRunAt?.ToLocalTime().ToString("g") ?? "not scheduled")}}
            </div>
          </form>
          """;
    }

    public static string RenderHandlers(IReadOnlyDictionary<string, HandlerDescriptor> handlers)
    {
        if (handlers.Count == 0)
            return """<div class="rounded-hpd border border-dashed border-hpd-line bg-white/70 p-3 text-sm text-hpd-muted">No handlers registered.</div>""";

        var sb = new StringBuilder();
        foreach (var (name, descriptor) in handlers.OrderBy(h => h.Key))
        {
            sb.Append($$"""
              <div class="rounded-hpd border border-hpd-line bg-white p-3">
                <div class="truncate text-sm font-black">{{H(descriptor.DisplayName)}}</div>
                <div class="mt-1 text-xs font-semibold text-hpd-muted">{{H(name)}} / {{H(descriptor.HandlerType)}}</div>
              </div>
              """);
        }

        return sb.ToString();
    }

    public static string RenderNotice(string message) => $$"""
      <div class="h-full rounded-hpd p-5">
        <div class="rounded-hpd border border-dashed border-hpd-line bg-white/70 p-3 text-sm text-hpd-muted">{{H(message)}}</div>
      </div>
      """;

    public static string EmptyJsonObjectIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? "{}" : value!;

    public static string? BlankToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string H(object? value) => WebUtility.HtmlEncode(value?.ToString() ?? "");

    private static string U(string value) => Uri.EscapeDataString(value);

    private static string Selected(bool selected) => selected ? "selected" : "";
}
