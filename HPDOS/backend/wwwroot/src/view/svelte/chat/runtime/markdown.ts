import DOMPurify from "dompurify";
import { Marked } from "marked";
import markedKatex from "marked-katex-extension";
import markedShiki from "marked-shiki";
import remend from "remend";
import { bundledLanguages, codeToHtml, type BundledLanguage } from "shiki";

const hpdCodeTokenColors = [
    {
      scope: ["comment", "punctuation.definition.comment", "string.comment"],
      settings: { foreground: "var(--hpd-code-comment)" }
    },
    {
      scope: ["string", "punctuation.definition.string", "string punctuation.section.embedded source"],
      settings: { foreground: "var(--hpd-code-string)" }
    },
    {
      scope: ["keyword", "storage", "storage.type"],
      settings: { foreground: "var(--hpd-code-keyword)" }
    },
    {
      scope: [
        "keyword.operator",
        "storage.type.function.arrow",
        "punctuation",
        "punctuation.separator",
        "meta.brace"
      ],
      settings: { foreground: "var(--hpd-code-punctuation)" }
    },
    {
      scope: ["constant", "constant.numeric", "constant.language", "entity.name.constant", "variable.language"],
      settings: { foreground: "var(--hpd-code-constant)" }
    },
    {
      scope: ["entity.name.function", "support.function", "support.type.primitive"],
      settings: { foreground: "var(--hpd-code-function)" }
    },
    {
      scope: ["entity.other.attribute-name", "meta.property-name", "support.type.property-name.css"],
      settings: { foreground: "var(--hpd-code-property)" }
    },
    {
      scope: ["entity.name", "entity.name.type", "support.class", "support.type", "support.class.component"],
      settings: { foreground: "var(--hpd-code-type)" }
    },
    {
      scope: ["variable", "variable.other", "variable.parameter.function"],
      settings: { foreground: "var(--hpd-code-variable)" }
    },
    {
      scope: ["support", "support.type.object.module", "variable.other.object"],
      settings: { foreground: "var(--hpd-code-object)" }
    },
    {
      scope: ["markup.inserted", "punctuation.definition.inserted", "meta.diff.header.to-file"],
      settings: { foreground: "var(--hpd-code-diff-add)" }
    },
    {
      scope: ["markup.deleted", "punctuation.definition.deleted", "meta.diff.header.from-file"],
      settings: { foreground: "var(--hpd-code-diff-delete)" }
    },
    {
      scope: ["invalid", "invalid.illegal", "message.error", "token.error-token"],
      settings: { foreground: "var(--hpd-code-critical)" }
    },
    {
      scope: ["markup.heading", "markup.heading entity.name", "token.info-token"],
      settings: { foreground: "var(--hpd-code-info)", fontStyle: "bold" }
    },
    {
      scope: ["markup.bold"],
      settings: { foreground: "var(--hpd-code-text)", fontStyle: "bold" }
    },
    {
      scope: ["markup.italic"],
      settings: { fontStyle: "italic" }
    }
  ];

const hpdCodeTheme = {
  name: "HPDChat",
  type: "dark",
  fg: "var(--hpd-code-text)",
  bg: "var(--hpd-code-background)",
  colors: {
    "editor.background": "var(--hpd-code-background)",
    "editor.foreground": "var(--hpd-code-text)",
    "gitDecoration.addedResourceForeground": "var(--hpd-code-diff-add)",
    "gitDecoration.deletedResourceForeground": "var(--hpd-code-diff-delete)"
  },
  settings: hpdCodeTokenColors,
  tokenColors: hpdCodeTokenColors,
  semanticTokenColors: {
    comment: "var(--hpd-code-comment)",
    string: "var(--hpd-code-string)",
    number: "var(--hpd-code-constant)",
    regexp: "var(--hpd-code-regexp)",
    keyword: "var(--hpd-code-keyword)",
    variable: "var(--hpd-code-variable)",
    parameter: "var(--hpd-code-variable)",
    property: "var(--hpd-code-property)",
    function: "var(--hpd-code-function)",
    method: "var(--hpd-code-function)",
    type: "var(--hpd-code-type)",
    class: "var(--hpd-code-type)",
    namespace: "var(--hpd-code-type)",
    enumMember: "var(--hpd-code-constant)"
  }
} as const;

const markdownParser = new Marked(
  {
    renderer: {
      link({ href, title, text }) {
        const safeHref = sanitizeUrl(href);
        if (!safeHref) return text;
        const titleAttribute = title ? ` title="${escapeAttribute(title)}"` : "";
        return `<a href="${escapeAttribute(safeHref)}"${titleAttribute} target="_blank" rel="noopener noreferrer">${text}</a>`;
      }
    }
  },
  markedKatex({
    throwOnError: false,
    nonStandard: true
  }),
  markedShiki({
    async highlight(code, lang) {
      const language = resolveLanguage(lang);
      return await codeToHtml(code, {
        lang: language,
        theme: hpdCodeTheme
      });
    }
  })
);

export async function renderMarkdown(markdown: string): Promise<string> {
  const healedMarkdown = remend(markdown.replace(/\r\n?/g, "\n"), { linkMode: "text-only" });
  const html = await markdownParser.parse(healedMarkdown);
  return sanitizeHtml(html);
}

function sanitizeHtml(html: string): string {
  if (DOMPurify.isSupported && typeof DOMPurify.sanitize === "function") {
    return DOMPurify.sanitize(html, {
      USE_PROFILES: { html: true, mathMl: true },
      SANITIZE_NAMED_PROPS: true,
      FORBID_TAGS: ["script", "style"],
      FORBID_CONTENTS: ["script", "style"],
      ADD_ATTR: ["target"]
    });
  }

  return fallbackSanitizeHtml(html);
}

function fallbackSanitizeHtml(html: string): string {
  return html
    .replace(/<script\b[\s\S]*?<\/script>/gi, "")
    .replace(/<style\b[\s\S]*?<\/style>/gi, "")
    .replace(/\son[a-z]+\s*=\s*(?:"[^"]*"|'[^']*'|[^\s>]+)/gi, "")
    .replace(/\s(?:href|src)\s*=\s*(["'])\s*javascript:[\s\S]*?\1/gi, "")
    .replace(/\s(?:href|src)\s*=\s*javascript:[^\s>]*/gi, "");
}

function resolveLanguage(lang: string | undefined): BundledLanguage | "text" {
  if (lang && lang in bundledLanguages) {
    return lang as BundledLanguage;
  }

  return "text";
}

function sanitizeUrl(href: string | null): string | undefined {
  if (!href) return undefined;
  try {
    const url = new URL(href, "https://hpd.local");
    if (url.protocol === "http:" || url.protocol === "https:" || url.protocol === "mailto:") {
      return href;
    }
  } catch {
    return undefined;
  }

  return undefined;
}

function escapeAttribute(value: string): string {
  return value.replace(/[&<>"']/g, (char) => {
    switch (char) {
      case "&":
        return "&amp;";
      case "<":
        return "&lt;";
      case ">":
        return "&gt;";
      case "\"":
        return "&quot;";
      default:
        return "&#39;";
    }
  });
}
