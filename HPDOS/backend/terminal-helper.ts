import { spawn } from "bun-pty";

type HelperCommand = {
  type?: string;
  terminalId?: string;
  command?: string;
  args?: string[];
  cwd?: string;
  env?: Record<string, string>;
  cols?: number;
  rows?: number;
  data?: string;
};

type PtyProcess = ReturnType<typeof spawn>;

const terminals = new Map<string, PtyProcess>();

function emit(value: Record<string, unknown>) {
  process.stdout.write(`${JSON.stringify(value)}\n`);
}

function fail(terminalId: string | undefined, message: unknown) {
  emit({
    type: "error",
    terminalId,
    message: message instanceof Error ? message.message : String(message)
  });
}

function create(command: HelperCommand) {
  if (!command.terminalId) throw new Error("terminalId is required");
  if (!command.command) throw new Error("command is required");
  if (terminals.has(command.terminalId)) throw new Error("terminal already exists");

  const pty = spawn(command.command, command.args ?? [], {
    name: "xterm-256color",
    cols: command.cols ?? 80,
    rows: command.rows ?? 24,
    cwd: command.cwd,
    env: {
      ...process.env,
      ...(command.env ?? {})
    }
  });

  terminals.set(command.terminalId, pty);
  emit({ type: "created", terminalId: command.terminalId, pid: pty.pid });
  pty.onData((data) => {
    emit({ type: "output", terminalId: command.terminalId, data });
  });
  pty.onExit((event) => {
    terminals.delete(command.terminalId!);
    emit({
      type: "exit",
      terminalId: command.terminalId,
      exitCode: event.exitCode,
      signal: event.signal
    });
  });
}

function write(command: HelperCommand) {
  const terminal = get(command.terminalId);
  terminal.write(command.data ?? "");
}

function resize(command: HelperCommand) {
  const terminal = get(command.terminalId);
  terminal.resize(command.cols ?? 80, command.rows ?? 24);
}

function kill(command: HelperCommand) {
  const terminal = get(command.terminalId);
  terminal.kill();
  terminals.delete(command.terminalId!);
}

function get(terminalId: string | undefined) {
  if (!terminalId) throw new Error("terminalId is required");
  const terminal = terminals.get(terminalId);
  if (!terminal) throw new Error(`terminal not found: ${terminalId}`);
  return terminal;
}

let pending = "";
const decoder = new TextDecoder();

for await (const chunk of Bun.stdin.stream()) {
  pending += decoder.decode(chunk, { stream: true });
  let newline = pending.indexOf("\n");
  while (newline >= 0) {
    const text = pending.slice(0, newline).trim();
    pending = pending.slice(newline + 1);
    if (text) handleLine(text);
    newline = pending.indexOf("\n");
  }
}

if (pending.trim()) handleLine(pending.trim());

function handleLine(text: string) {
  let command: HelperCommand | undefined;
  try {
    command = JSON.parse(text) as HelperCommand;
    switch (command.type) {
      case "create":
        create(command);
        break;
      case "write":
        write(command);
        break;
      case "resize":
        resize(command);
        break;
      case "kill":
        kill(command);
        break;
      default:
        throw new Error(`unknown command type: ${command.type}`);
    }
  } catch (error) {
    fail(command?.terminalId, error);
  }
}
