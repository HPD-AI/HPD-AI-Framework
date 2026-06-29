window.HPD_AI_STUDIO_CONFIG = {
  apiBasePath: "/api/hpd",
  routePrefix: "/studio",
  productTitle: "HPD AI Studio",
  mode: "development",
  capabilities: [
    "agents", "sessions", "threads", "streaming", "content", "multi-agent", "agent-evals",
    "graphs", "workflows", "rag", "retrieval", "indexes",
    "auth", "identity", "access-control",
    "ml", "models", "training", "evaluations"
  ],
  studioModules: [
    { id: "agents", label: "Agents", title: "HPD Agent Studio", status: "active" },
    { id: "workflows", label: "Workflows", title: "HPD Graph Studio", status: "active" },
    { id: "rag", label: "RAG", title: "HPD RAG Studio", status: "active" },
    { id: "auth", label: "Auth", title: "HPD Auth Studio", status: "active" },
    { id: "ml", label: "ML", title: "HPD ML Studio", status: "active" }
  ]
};
