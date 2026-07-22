namespace HPDOS.ToolHarnesses.Middleware;

/// <summary>Provides HTML language intelligence.</summary>
[HpdLanguageServer("html")]
[LanguageServerExtensions(".html", ".htm")]
[LanguageServerLanguageIds(".html", "html", ".htm", "html")]
[LanguageServerRootMarkers("package.json", ".git")]
[LanguageServerExecutable("vscode-html-language-server")]
[LanguageServerArguments("--stdio")]
public sealed class HtmlLanguageServer;

/// <summary>Provides CSS-family language intelligence.</summary>
[HpdLanguageServer("css")]
[LanguageServerExtensions(".css", ".scss", ".sass", ".less")]
[LanguageServerRootMarkers("package.json", ".git")]
[LanguageServerExecutable("vscode-css-language-server")]
[LanguageServerArguments("--stdio")]
public sealed class CssLanguageServer;

/// <summary>Provides JSON language intelligence.</summary>
[HpdLanguageServer("json")]
[LanguageServerExtensions(".json", ".jsonc")]
[LanguageServerLanguageIds(".json", "json", ".jsonc", "jsonc")]
[LanguageServerRootMarkers("package.json", ".git")]
[LanguageServerExecutable("vscode-json-language-server")]
[LanguageServerArguments("--stdio")]
public sealed class JsonLanguageServer;

/// <summary>Provides YAML language intelligence.</summary>
[HpdLanguageServer("yaml")]
[LanguageServerExtensions(".yaml", ".yml")]
[LanguageServerLanguageIds(".yaml", "yaml", ".yml", "yaml")]
[LanguageServerRootMarkers(".git")]
[LanguageServerExecutable("yaml-language-server")]
[LanguageServerArguments("--stdio")]
public sealed class YamlLanguageServer;

/// <summary>Provides JavaScript and TypeScript diagnostics through ESLint.</summary>
[HpdLanguageServer("eslint")]
[LanguageServerExtensions(".ts", ".tsx", ".js", ".jsx", ".mjs", ".cjs", ".vue", ".svelte")]
[LanguageServerRootMarkers(".eslintrc", ".eslintrc.js", ".eslintrc.json", ".eslintrc.yml", "eslint.config.js", "eslint.config.mjs")]
[LanguageServerExecutable("vscode-eslint-language-server")]
[LanguageServerArguments("--stdio")]
public sealed class EslintLanguageServer;

/// <summary>Provides Biome diagnostics through its LSP proxy.</summary>
[HpdLanguageServer("biome")]
[LanguageServerExtensions(".ts", ".tsx", ".js", ".jsx", ".mjs", ".cjs", ".json", ".jsonc")]
[LanguageServerRootMarkers("biome.json", "biome.jsonc")]
[LanguageServerExecutable("biome")]
[LanguageServerArguments("lsp-proxy")]
public sealed class BiomeLanguageServer;

/// <summary>Provides Tailwind CSS language intelligence.</summary>
[HpdLanguageServer("tailwindcss")]
[LanguageServerExtensions(".html", ".css", ".scss", ".js", ".jsx", ".ts", ".tsx", ".vue", ".svelte")]
[LanguageServerRootMarkers("tailwind.config.js", "tailwind.config.ts", "tailwind.config.mjs", "tailwind.config.cjs")]
[LanguageServerExecutable("tailwindcss-language-server")]
[LanguageServerArguments("--stdio")]
public sealed class TailwindCssLanguageServer;

/// <summary>Provides Emmet completions for markup and stylesheet files.</summary>
[HpdLanguageServer("emmet-language-server")]
[LanguageServerExtensions(".html", ".css", ".scss", ".less", ".jsx", ".tsx", ".vue", ".svelte")]
[LanguageServerRootMarkers(".git")]
[LanguageServerExecutable("emmet-language-server")]
[LanguageServerArguments("--stdio")]
public sealed class EmmetLanguageServer;

/// <summary>Provides Vue language intelligence.</summary>
[HpdLanguageServer("vue")]
[LanguageServerExtensions(".vue")]
[LanguageServerLanguageIds(".vue", "vue")]
[LanguageServerRootMarkers("vue.config.js", "nuxt.config.js", "nuxt.config.ts", "package.json")]
[LanguageServerExecutable("vue-language-server")]
[LanguageServerArguments("--stdio")]
public sealed class VueLanguageServer;

/// <summary>Provides Svelte language intelligence.</summary>
[HpdLanguageServer("svelte")]
[LanguageServerExtensions(".svelte")]
[LanguageServerLanguageIds(".svelte", "svelte")]
[LanguageServerRootMarkers("svelte.config.js", "svelte.config.mjs", "package.json")]
[LanguageServerExecutable("svelteserver")]
[LanguageServerArguments("--stdio")]
public sealed class SvelteLanguageServer;

/// <summary>Provides Astro language intelligence.</summary>
[HpdLanguageServer("astro")]
[LanguageServerExtensions(".astro")]
[LanguageServerLanguageIds(".astro", "astro")]
[LanguageServerRootMarkers("astro.config.mjs", "astro.config.js", "astro.config.ts")]
[LanguageServerExecutable("astro-ls")]
[LanguageServerArguments("--stdio")]
public sealed class AstroLanguageServer;

/// <summary>Provides GraphQL language intelligence.</summary>
[HpdLanguageServer("graphql")]
[LanguageServerExtensions(".graphql", ".gql")]
[LanguageServerLanguageIds(".graphql", "graphql", ".gql", "graphql")]
[LanguageServerRootMarkers(".graphqlrc", ".graphqlrc.json", ".graphqlrc.yml", ".graphqlrc.yaml", "graphql.config.js")]
[LanguageServerExecutable("graphql-lsp")]
[LanguageServerArguments("server", "-m", "stream")]
public sealed class GraphQlLanguageServer;

/// <summary>Provides Prisma schema language intelligence.</summary>
[HpdLanguageServer("prisma")]
[LanguageServerExtensions(".prisma")]
[LanguageServerLanguageIds(".prisma", "prisma")]
[LanguageServerRootMarkers("schema.prisma")]
[LanguageServerExecutable("prisma-language-server")]
[LanguageServerArguments("--stdio")]
public sealed class PrismaLanguageServer;
