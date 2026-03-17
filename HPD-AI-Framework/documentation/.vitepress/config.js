import { defineConfig } from 'vitepress'

export default defineConfig({
  title: 'HPD AI',
  description: 'Build production-ready AI applications in .NET',

  base: '/HPD-AI-Framework/',
  srcDir: '.',
  cleanUrls: true,

  themeConfig: {
    logo: '/logo.svg',

    nav: [
      { text: 'Home', link: '/' },
      {
        text: 'HPD-Agent',
        items: [
          { text: 'Getting Started', link: '/hpd-agent/Getting Started/00 Agents Overview' },
          { text: 'Tools', link: '/hpd-agent/Tools/02.1 CSharp Tools Overview' },
          { text: 'Middleware', link: '/hpd-agent/Middleware/04.1 Middleware Lifecycle' },
          { text: 'Events', link: '/hpd-agent/Events/05.1 Events Overview' },
          { text: 'Multi-Agent', link: '/hpd-agent/Multi-Agent/06.1 Overview' },
          { text: 'Cookbook', link: '/hpd-agent/Cookbook/01 Hello World Chat Loop' },
        ]
      },
      { text: 'HPD-Auth', link: '/hpd-auth/' },
      { text: 'HPD-RAG', link: '/hpd-rag/' },
      { text: 'HPD-ML', link: '/hpd-ml/' },
    ],

    sidebar: {
      '/hpd-agent/': [
        {
          text: 'Getting Started',
          collapsed: false,
          items: [
            { text: 'Agents Overview', link: '/hpd-agent/Getting Started/00 Agents Overview' },
            { text: 'Customizing an Agent', link: '/hpd-agent/Getting Started/01 Customizing an Agent' },
            { text: 'Multi-Turn Conversations', link: '/hpd-agent/Getting Started/02 Multi-Turn Conversations' },
            { text: 'Tool Calling', link: '/hpd-agent/Getting Started/03 Tool Calling' },
            { text: 'Middleware', link: '/hpd-agent/Getting Started/04 Middleware' },
            { text: 'Event Handling', link: '/hpd-agent/Getting Started/05 Event Handling' },
            { text: 'Memory', link: '/hpd-agent/Getting Started/06 Memory' },
            { text: 'Building Console Apps', link: '/hpd-agent/Getting Started/07 Building Console Apps' },
            { text: 'Building Web Apps', link: '/hpd-agent/Getting Started/08 Building Web Apps' },
          ]
        },
        {
          text: 'Tools',
          collapsed: false,
          items: [
            { text: 'C# Tools Overview', link: '/hpd-agent/Tools/02.1 CSharp Tools Overview' },
            { text: 'AIFunctions', link: '/hpd-agent/Tools/02.1.1 AIFunctions' },
            { text: 'Skills', link: '/hpd-agent/Tools/02.1.2 Skills' },
            { text: 'SubAgents', link: '/hpd-agent/Tools/02.1.3 SubAgents' },
            { text: 'Tool Dynamic Metadata', link: '/hpd-agent/Tools/02.1.4 Tool Dynamic Metadata' },
            { text: 'Context Engineering', link: '/hpd-agent/Tools/02.1.5 Context Engineering' },
            { text: 'Writing Instructions', link: '/hpd-agent/Tools/02.1.6 Writing Instructions' },
            { text: 'Conditional Expression DSL', link: '/hpd-agent/Tools/02.1.7 Conditional Expression DSL' },
            { text: 'MultiAgent', link: '/hpd-agent/Tools/02.1.8 MultiAgent' },
            { text: 'MCP Servers', link: '/hpd-agent/Tools/02.2 MCP Servers' },
            { text: 'Client Tools', link: '/hpd-agent/Tools/02.3 Client Tools' },
            { text: 'OpenAPI Tools', link: '/hpd-agent/Tools/02.4 OpenAPI Tools' },
          ]
        },
        {
          text: 'Middleware',
          collapsed: false,
          items: [
            { text: 'Middleware Lifecycle', link: '/hpd-agent/Middleware/04.1 Middleware Lifecycle' },
            { text: 'Middleware State', link: '/hpd-agent/Middleware/04.2 Middleware State' },
            { text: 'Middleware Events', link: '/hpd-agent/Middleware/04.3 Middleware Events' },
            { text: 'Built-in Middleware', link: '/hpd-agent/Middleware/04.4 Built-in Middleware' },
            { text: 'Custom Middleware', link: '/hpd-agent/Middleware/04.5 Custom Middleware' },
          ]
        },
        {
          text: 'Events',
          collapsed: false,
          items: [
            { text: 'Events Overview', link: '/hpd-agent/Events/05.1 Events Overview' },
            { text: 'Event Types Reference', link: '/hpd-agent/Events/05.2 Event Types Reference' },
            { text: 'Consuming Events', link: '/hpd-agent/Events/05.3 Consuming Events' },
            { text: 'SubAgent Events', link: '/hpd-agent/Events/05.4 SubAgent Events' },
            { text: 'Streaming & Cancellation', link: '/hpd-agent/Events/05.5 Streaming & Cancellation' },
            { text: 'Bidirectional Events', link: '/hpd-agent/Events/05.6 Bidirectional Events' },
            { text: 'Custom Events', link: '/hpd-agent/Events/05.7 Custom Events' },
          ]
        },
        {
          text: 'Multi-Agent',
          collapsed: false,
          items: [
            { text: 'Overview', link: '/hpd-agent/Multi-Agent/06.1 Overview' },
            { text: 'Building Workflows', link: '/hpd-agent/Multi-Agent/06.2 Building Workflows' },
            { text: 'Node Options', link: '/hpd-agent/Multi-Agent/06.3 Node Options' },
            { text: 'Routing & Edges', link: '/hpd-agent/Multi-Agent/06.4 Routing & Edges' },
            { text: 'As a Toolkit Capability', link: '/hpd-agent/Multi-Agent/06.5 As a Toolkit Capability' },
            { text: 'Workflow Events', link: '/hpd-agent/Multi-Agent/06.6 Workflow Events' },
            { text: 'Observability', link: '/hpd-agent/Multi-Agent/06.7 Observability' },
          ]
        },
        {
          text: 'Cookbook',
          collapsed: false,
          items: [
            { text: 'Hello World: Chat Loop', link: '/hpd-agent/Cookbook/01 Hello World Chat Loop' },
            { text: 'Building a Toolkit', link: '/hpd-agent/Cookbook/02 Building a Toolkit' },
            { text: 'Managing Context with Collapsing', link: '/hpd-agent/Cookbook/03 Managing Context with Collapsing' },
            { text: 'One Agent, Many Experts', link: '/hpd-agent/Cookbook/04 One Agent, Many Experts' },
            { text: 'Writing Middleware', link: '/hpd-agent/Cookbook/05 Writing Middleware' },
            { text: 'Ralph Wiggum Loops', link: '/hpd-agent/Cookbook/06 Ralph Wiggum Loops with Middleware' },
          ]
        },
        {
          text: 'Agent Builder & Config',
          collapsed: false,
          items: [
            { text: 'Agent Builder', link: '/hpd-agent/Agent Builder & Config/Agent Builder' },
            { text: 'Agent Config', link: '/hpd-agent/Agent Builder & Config/Agent Config' },
            { text: 'Run Config', link: '/hpd-agent/Agent Builder & Config/Run Config' },
            { text: 'Error Handling', link: '/hpd-agent/Agent Builder & Config/Error Handling' },
            { text: 'History Reduction', link: '/hpd-agent/Agent Builder & Config/History Reduction' },
            { text: 'Caching', link: '/hpd-agent/Agent Builder & Config/Caching' },
            { text: 'Collapsing', link: '/hpd-agent/Agent Builder & Config/Collapsing' },
            { text: 'Observability', link: '/hpd-agent/Agent Builder & Config/Observability' },
            { text: 'Session Store', link: '/hpd-agent/Agent Builder & Config/Session Store' },
            { text: 'Sandbox Config', link: '/hpd-agent/Agent Builder & Config/Sandbox Config' },
            {
              text: 'Providers',
              collapsed: true,
              items: [
                { text: 'Providers Overview', link: '/hpd-agent/Agent Builder & Config/Providers/00 Providers Overview' },
                { text: 'Anthropic', link: '/hpd-agent/Agent Builder & Config/Providers/Anthropic' },
                { text: 'OpenAI', link: '/hpd-agent/Agent Builder & Config/Providers/OpenAI' },
                { text: 'Azure AI', link: '/hpd-agent/Agent Builder & Config/Providers/AzureAI' },
                { text: 'Google AI', link: '/hpd-agent/Agent Builder & Config/Providers/GoogleAI' },
                { text: 'Bedrock', link: '/hpd-agent/Agent Builder & Config/Providers/Bedrock' },
                { text: 'Mistral', link: '/hpd-agent/Agent Builder & Config/Providers/Mistral' },
                { text: 'HuggingFace', link: '/hpd-agent/Agent Builder & Config/Providers/Huggingface' },
                { text: 'Ollama', link: '/hpd-agent/Agent Builder & Config/Providers/Ollama' },
                { text: 'ONNX Runtime', link: '/hpd-agent/Agent Builder & Config/Providers/OnnxRuntime' },
              ]
            }
          ]
        }
      ],

      '/hpd-auth/': [
        {
          text: 'Getting Started',
          collapsed: false,
          items: [
            { text: 'Introduction', link: '/hpd-auth/Getting Started/00 Introduction' },
            { text: 'Installation', link: '/hpd-auth/Getting Started/01 Installation' },
            { text: 'Quick Start', link: '/hpd-auth/Getting Started/02 Quick Start' },
            { text: 'Configuration Reference', link: '/hpd-auth/Getting Started/03 Configuration Reference' },
          ]
        },
        {
          text: 'Core Concepts',
          collapsed: false,
          items: [
            { text: 'Authentication', link: '/hpd-auth/Core Concepts/01 Authentication' },
            { text: 'Sessions', link: '/hpd-auth/Core Concepts/02 Sessions' },
            { text: 'User Model', link: '/hpd-auth/Core Concepts/03 User Model' },
            { text: 'Events', link: '/hpd-auth/Core Concepts/04 Events' },
          ]
        },
        {
          text: 'Guides',
          collapsed: false,
          items: [
            { text: 'Send Emails', link: '/hpd-auth/Guides/01 Send Emails' },
            { text: 'Send SMS', link: '/hpd-auth/Guides/02 Send SMS' },
            { text: 'Auth Events', link: '/hpd-auth/Guides/03 Auth Events' },
            { text: 'Protect Endpoints', link: '/hpd-auth/Guides/04 Protect Endpoints' },
            { text: 'Multi-Tenancy', link: '/hpd-auth/Guides/06 Multi-Tenancy' },
          ]
        },
        {
          text: 'Packages',
          collapsed: false,
          items: [
            { text: 'Overview', link: '/hpd-auth/Packages/00 Overview' },
            { text: 'HPD.Auth', link: '/hpd-auth/Packages/01 HPD.Auth' },
            { text: 'Authentication', link: '/hpd-auth/Packages/02 Authentication' },
            { text: 'TwoFactor', link: '/hpd-auth/Packages/03 TwoFactor' },
            { text: 'OAuth', link: '/hpd-auth/Packages/04 OAuth' },
            { text: 'Admin', link: '/hpd-auth/Packages/05 Admin' },
            { text: 'Authorization', link: '/hpd-auth/Packages/06 Authorization' },
            { text: 'Audit', link: '/hpd-auth/Packages/07 Audit' },
          ]
        },
        {
          text: 'Security',
          collapsed: false,
          items: [
            { text: 'Session Revocation', link: '/hpd-auth/Security/01 Session Revocation' },
            { text: 'Metadata', link: '/hpd-auth/Security/02 Metadata' },
            { text: 'Password Policy', link: '/hpd-auth/Security/03 Password Policy' },
            { text: 'Responsible Disclosure', link: '/hpd-auth/Security/04 Responsible Disclosure' },
          ]
        },
        {
          text: 'API Reference',
          collapsed: false,
          items: [
            { text: 'Overview', link: '/hpd-auth/API Reference/00 Overview' },
            { text: 'Auth', link: '/hpd-auth/API Reference/01 Auth' },
            { text: 'Sessions', link: '/hpd-auth/API Reference/02 Sessions' },
            { text: 'TwoFactor', link: '/hpd-auth/API Reference/03 TwoFactor' },
            { text: 'Passkeys', link: '/hpd-auth/API Reference/04 Passkeys' },
            { text: 'OAuth', link: '/hpd-auth/API Reference/05 OAuth' },
            { text: 'Admin', link: '/hpd-auth/API Reference/06 Admin' },
          ]
        },
      ],

      '/hpd-rag/': [],
      '/hpd-ml/': [],
    },

    socialLinks: [
      { icon: 'github', link: 'https://github.com/HPD-AI/hpd-ai-framework' }
    ],

    footer: {
      message: 'Released under the AGPL-3.0 License.',
      copyright: 'Copyright © 2026 HPD AI'
    },

    search: {
      provider: 'local'
    },

    editLink: {
      pattern: 'https://github.com/HPD-AI/hpd-ai-framework/edit/main/documentation/:path',
      text: 'Edit this page on GitHub'
    }
  },

  markdown: {
    theme: {
      light: 'github-light',
      dark: 'github-dark'
    },
    lineNumbers: true
  },

  head: [
    ['link', { rel: 'icon', href: '/favicon.ico' }],
    ['meta', { name: 'theme-color', content: '#14B8A6' }],
    ['meta', { property: 'og:title', content: 'HPD AI' }],
    ['meta', { property: 'og:description', content: 'Build production-ready AI applications in .NET' }],
  ]
})
