// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

namespace HPD.Agent;

internal static class FunctionCollapseSelectorResolver
{
    internal static void Validate(
        IEnumerable<string>? selectors,
        IEnumerable<ToolHarnessFactory> factories)
    {
        if (selectors is null)
            return;
        var available = factories
            .GroupBy(static factory => factory.ActivationIdentity, StringComparer.Ordinal)
            .Select(static group => group.First())
            .ToArray();

        foreach (var raw in selectors)
        {
            if (string.IsNullOrWhiteSpace(raw))
                throw Invalid(raw, "Function-collapse selector cannot be empty.");
            var selector = raw.Trim();
            var canonicalSeparator = selector.LastIndexOf("::", StringComparison.Ordinal);
            string owner;
            string member;
            bool canonical;
            if (canonicalSeparator > 0)
            {
                canonical = true;
                owner = selector[..canonicalSeparator];
                member = selector[(canonicalSeparator + 2)..];
            }
            else
            {
                var memberSeparator = selector.LastIndexOf('.');
                if (memberSeparator <= 0 || memberSeparator == selector.Length - 1)
                    throw Invalid(selector,
                        "Function-collapse selectors must use 'ToolHarness.Member' or '{OwnerIdentity}::Member'.");
                canonical = false;
                owner = selector[..memberSeparator];
                member = selector[(memberSeparator + 1)..];
            }

            var owners = available.Where(factory =>
                    canonical
                        ? string.Equals(factory.ActivationIdentity, owner, StringComparison.OrdinalIgnoreCase)
                        : string.Equals(factory.Name, owner, StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(factory.ToolHarnessType.Name, owner, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (owners.Length == 0)
                continue; // Optional harness is not registered for this agent.

            var matches = owners
                .SelectMany(factory => (factory.CollapsibleFunctionDeclarations ?? string.Empty)
                    .Split(';', StringSplitOptions.RemoveEmptyEntries)
                    .Where(declaration => string.Equals(declaration, member, StringComparison.OrdinalIgnoreCase))
                    .Select(declaration => new { Factory = factory, Declaration = declaration }))
                .ToArray();
            if (matches.Length == 0)
                throw Invalid(selector,
                    $"Function '{member}' in selected ToolHarness '{owners[0].Name}' is not declared with CollapseFunction.");
            if (matches.Length > 1 ||
                matches.Select(static match => match.Factory.ActivationIdentity)
                    .Distinct(StringComparer.Ordinal).Count() > 1)
                throw Invalid(selector,
                    $"Function-collapse selector '{selector}' is ambiguous across selected ToolHarnesses.");
        }
    }

    private static AgentRunConfigurationException Invalid(string selector, string message)
        => new(
            "function_collapse_selector_invalid",
            nameof(CollapsingConfig.NeverCollapseFunctions),
            $"{message} Selector: '{selector}'.");
}
