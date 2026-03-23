// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

// Polyfill for C# 9 record types in .NET Standard 2.0
// This type is required by the compiler to support init-only properties and records

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("HPD-Agent.Tests")]

namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
