window.HPD_AI_PLATFORM_CONFIG = {
  apiBasePath: "/api/hpd",
  routePrefix: "/studio",
  productTitle: "HPD AI Platform",
  mode: "development",
  capabilities: [
    "agents", "sessions", "threads", "streaming", "content", "multi-agent", "agent-evals",
    "graphs", "workflows", "rag", "retrieval", "indexes",
    "auth", "identity", "access-control",
    "ml", "models", "training", "evaluations",
    "base", "records", "collections", "schemas", "stores", "files", "realtime", "policy", "health", "diagnostics"
  ],
  studioModules: [
    { id: "agents", label: "Agents", title: "HPD Agent Studio", status: "active" },
    { id: "workflows", label: "Workflows", title: "HPD Graph Studio", status: "active" },
    { id: "rag", label: "RAG", title: "HPD RAG Studio", status: "active" },
    { id: "auth", label: "Auth", title: "HPD Auth Studio", status: "active" },
    { id: "ml", label: "ML", title: "HPD ML Studio", status: "active" },
    { id: "base", label: "BASE", title: "HPD BASE Studio", status: "active" }
  ]
};
