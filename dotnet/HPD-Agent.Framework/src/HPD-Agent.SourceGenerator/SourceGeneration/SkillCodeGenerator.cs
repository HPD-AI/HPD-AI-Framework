using System.Collections.Generic;
using System.Linq;
using System.Text;

/// <summary>
/// Generates code for skill registration
/// </summary>
internal static class SkillCodeGenerator
{
    /// <summary>
    /// Generates the GetReferencedToolHarnesses() method for auto-registration
    /// PHASE 5: Now uses SkillCapabilities (fully populated with resolved references)
    /// </summary>
    public static string GenerateGetReferencedToolHarnessesMethod(ToolHarnessInfo ToolHarness)
    {
        if (!ToolHarness.SkillCapabilities.Any())
            return string.Empty;

        var allReferencedToolHarnesses = ToolHarness.SkillCapabilities
            .SelectMany(s => s.ResolvedToolHarnessTypes)
            .Distinct()
            .OrderBy(p => p)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// Gets the list of ToolHarnesses referenced by skills in this class");
        sb.AppendLine("        /// Used by AgentBuilder for auto-registration");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        public static string[] GetReferencedToolHarnesses()");

        if (!allReferencedToolHarnesses.Any())
        {
            sb.AppendLine("            => Array.Empty<string>();");
            return sb.ToString();
        }
        sb.AppendLine("        {");
        sb.AppendLine("            return new string[]");
        sb.AppendLine("            {");

        for (int i = 0; i < allReferencedToolHarnesses.Count; i++)
        {
            var comma = i < allReferencedToolHarnesses.Count - 1 ? "," : "";
            sb.AppendLine($"                \"{allReferencedToolHarnesses[i]}\"{comma}");
        }

        sb.AppendLine("            };");
        sb.AppendLine("        }");

        return sb.ToString();
    }

    /// <summary>
    /// Generates the GetReferencedFunctions() method for selective function registration
    /// PHASE 5: Now uses SkillCapabilities (fully populated with resolved references)
    /// </summary>
    public static string GenerateGetReferencedFunctionsMethod(ToolHarnessInfo ToolHarness)
    {
        if (!ToolHarness.SkillCapabilities.Any())
            return string.Empty;

        // Build dictionary: ToolHarnessName -> HashSet<FunctionName>
        var toolFunctions = new Dictionary<string, HashSet<string>>();

        foreach (var skill in ToolHarness.SkillCapabilities)
        {
            foreach (var funcRef in skill.ResolvedFunctionReferences)
            {
                // "FileSystemToolHarness.ReadFile" -> ("FileSystemToolHarness", "ReadFile")
                var parts = funcRef.Split('.');
                if (parts.Length == 2)
                {
                    var toolName = parts[0];
                    var functionName = parts[1];

                    if (!toolFunctions.ContainsKey(toolName))
                        toolFunctions[toolName] = new HashSet<string>();

                    toolFunctions[toolName].Add(functionName);
                }
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// Gets the specific functions referenced by skills (for selective registration)");
        sb.AppendLine("        /// Used by AgentBuilder to register only needed functions from each ToolHarness");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        public static Dictionary<string, string[]> GetReferencedFunctions()");

        if (!toolFunctions.Any())
        {
            sb.AppendLine("            => new Dictionary<string, string[]>();");
            return sb.ToString();
        }
        sb.AppendLine("        {");
        sb.AppendLine("            return new Dictionary<string, string[]>");
        sb.AppendLine("            {");

        var entries = toolFunctions.OrderBy(kvp => kvp.Key).ToList();
        for (int i = 0; i < entries.Count; i++)
        {
            var comma = i < entries.Count - 1 ? "," : "";
            var functions = string.Join("\", \"", entries[i].Value.OrderBy(f => f));
            sb.AppendLine($"                {{ \"{entries[i].Key}\", new string[] {{ \"{functions}\" }} }}{comma}");
        }

        sb.AppendLine("            };");
        sb.AppendLine("        }");

        return sb.ToString();
    }

    /// <summary>
    /// Generates skill registration code to be added to CreateToolHarness() method
    /// Handles both class-level collapsing (if [ToolHarness] on class with Collapsed=true) and individual skill containers
    /// </summary>
    public static string GenerateSkillRegistrations(ToolHarnessInfo ToolHarness)
    {
        // Early exit ONLY if no skills AND not collapsed
        // If ToolHarness is collapsed, we need to register the container even without skills
        if (!ToolHarness.SkillCapabilities.Any() && !ToolHarness.IsCollapsed)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine();

        // If the ToolHarness is collapsed, create a class-level container first
        if (ToolHarness.IsCollapsed)
        {
            sb.AppendLine("        // Register toolharness container");
            // Method name uses ClassName; the container's Name property uses EffectiveName
            sb.AppendLine($"        functions.Add(Create{ToolHarness.ClassName}Container(instance, serialization));");
            sb.AppendLine();
        }

        // Early exit if no skills to register (but after registering collapse container if needed)
        if (!ToolHarness.SkillCapabilities.Any())
            return sb.ToString();

        sb.AppendLine("        // Register skill containers");

        foreach (var skill in ToolHarness.SkillCapabilities)
        {
            var owner = string.IsNullOrEmpty(skill.ParentNamespace)
                ? skill.ParentToolHarnessName
                : $"{skill.ParentNamespace}.{skill.ParentToolHarnessName}";
            var skillExpression = skill.MethodIsStatic
                ? $"{ToolHarness.ClassName}.{skill.MethodName}()"
                : $"instance.{skill.MethodName}()";

            // Check if skill has conditional registration (same pattern as Functions/SubAgents)
            var hasConditionalEvaluator = skill.IsConditional &&
                                        skill.HasTypedMetadata;

            if (hasConditionalEvaluator)
            {
                sb.AppendLine($"        if (Evaluate{skill.Name}Condition(context))");
                sb.AppendLine("        {");
                sb.AppendLine($"            functions.Add(Create{skill.MethodName}Skill(instance, context, serialization));");
                sb.AppendLine($"            functions.AddRange(SkillCapabilityFunctionProjector.CreateChildren(");
                sb.AppendLine($"                {skillExpression},");
                sb.AppendLine($"                CapabilityId.Create(@\"generated:{owner}:{skill.Name}\"),");
                sb.AppendLine("                serialization));");
                sb.AppendLine("        }");
            }
            else
            {
                // Each skill generates exactly one container function
                sb.AppendLine($"        functions.Add(Create{skill.MethodName}Skill(instance, context, serialization));");
                sb.AppendLine($"        functions.AddRange(SkillCapabilityFunctionProjector.CreateChildren(");
                sb.AppendLine($"            {skillExpression},");
                sb.AppendLine($"            CapabilityId.Create(@\"generated:{owner}:{skill.Name}\"),");
                sb.AppendLine("            serialization));");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Generates skill container function.
    /// Skills ARE containers - there's only one function per skill.
    /// PHASE 5: Now accepts SkillCapability instead of SkillInfo
    /// </summary>
    public static string GenerateSkillContainerFunction(HPD.Agent.SourceGenerator.Capabilities.SkillCapability skill, ToolHarnessInfo ToolHarness)
    {
        var sb = new StringBuilder();
        var owner = string.IsNullOrEmpty(skill.ParentNamespace)
            ? skill.ParentToolHarnessName
            : $"{skill.ParentNamespace}.{skill.ParentToolHarnessName}";
        var skillId = $"generated:{owner}:{skill.Name}";
        var skillExpression = skill.MethodIsStatic
            ? $"{ToolHarness.ClassName}.{skill.MethodName}()"
            : $"instance.{skill.MethodName}()";
        var descriptionCode = skill.HasDynamicDescription
            ? $"Resolve{skill.Name}Description(context)"
            : $"@\"{skill.Description.Replace("\"", "\"\"")}\"";

        sb.AppendLine($"        /// <summary>");
        sb.AppendLine($"        /// Creates the activation function for the {skill.Name} skill.");
        sb.AppendLine($"        /// </summary>");
        sb.AppendLine($"        /// <param name=\"instance\">The owning tool harness instance.</param>");
        sb.AppendLine($"        /// <param name=\"context\">Metadata used for conditional descriptions.</param>");
        sb.AppendLine($"        /// <param name=\"serialization\">Serialization configuration.</param>");
        sb.AppendLine($"        /// <returns>The generated activation function.</returns>");
        sb.AppendLine($"        private static AIFunction Create{skill.MethodName}Skill({ToolHarness.ClassName} instance, IToolMetadata? context, HPDToolSerializationOptions? serialization)");
        sb.AppendLine("        {");
        sb.AppendLine($"            var skillDefinition = {skillExpression};");
        sb.AppendLine("            return HPDAIFunctionFactory.Create(");
        sb.AppendLine("                async (arguments, functionContext, cancellationToken) =>");
        sb.AppendLine("                {");
        sb.AppendLine($"                    var capabilityId = CapabilityId.Create(@\"{skillId.Replace("\"", "\"\"")}\");");
        sb.AppendLine("                    await functionContext.PublishAsync(new SkillActivationStartedEvent(capabilityId, skillDefinition.Name), cancellationToken).ConfigureAwait(false);");
        sb.AppendLine("                    try");
        sb.AppendLine("                    {");
        sb.AppendLine("                        var instructionContext = new SkillInstructionContext(functionContext, functionContext.Services, functionContext.ContentStore);");
        sb.AppendLine("                        var instructions = await skillDefinition.Instructions(instructionContext, cancellationToken).ConfigureAwait(false);");
        sb.AppendLine("                        if (skillDefinition.Reinforcement is not null)");
        sb.AppendLine("                        {");
        sb.AppendLine("                            var reinforcement = await skillDefinition.Reinforcement(instructionContext, cancellationToken).ConfigureAwait(false);");
        sb.AppendLine("                            functionContext.ResultMetadata.Set(\"HPD.SkillReinforcement\", reinforcement);");
        sb.AppendLine("                        }");
        sb.AppendLine("                        await functionContext.PublishAsync(new SkillActivatedEvent(capabilityId, skillDefinition.Name, skillDefinition.Capabilities.Count, skillDefinition.Lifetime), cancellationToken).ConfigureAwait(false);");
        sb.AppendLine("                        return instructions;");
        sb.AppendLine("                    }");
        sb.AppendLine("                    catch (Exception exception) when (exception is not OperationCanceledException)");
        sb.AppendLine("                    {");
        sb.AppendLine("                        await functionContext.PublishAsync(new SkillActivationFailedEvent(capabilityId, skillDefinition.Name, exception.GetType().Name), CancellationToken.None).ConfigureAwait(false);");
        sb.AppendLine("                        throw;");
        sb.AppendLine("                    }");
        sb.AppendLine("                },");
        sb.AppendLine("                new HPDAIFunctionFactoryOptions");
        sb.AppendLine("                {");
        sb.AppendLine($"                    Name = \"{skill.Name}\",");
        sb.AppendLine($"                    Description = {descriptionCode},");
        if (skill.RequiresPermission)
            sb.AppendLine($"                    FunctionPermission = new global::HPD.Agent.AIFunctionPermissionDeclaration {{ RequiresPermission = true, Scope = \"function/{Uri.EscapeDataString(skill.Name)}\", Source = global::HPD.Agent.PermissionDeclarationSource.FrameworkDefault }},");
        sb.AppendLine("                    SchemaProvider = () => CreateEmptyContainerSchema(),");
        sb.AppendLine("                    SerializerOptions = serialization?.SerializerOptions,");
        sb.AppendLine("                    ResultType = typeof(string),");
        sb.AppendLine("                    AdditionalProperties = new Dictionary<string, object>");
        sb.AppendLine("                    {");
        sb.AppendLine("                        [HPDCapabilityMetadata.AdditionalPropertiesKey] = new HPDCapabilityMetadata");
        sb.AppendLine("                        {");
        sb.AppendLine($"                            Id = CapabilityId.Create(@\"{skillId.Replace("\"", "\"\"")}\"),");
        sb.AppendLine("                            Kind = HPDCapabilityKind.SkillActivation,");
        if (ToolHarness.IsCollapsed)
            sb.AppendLine($"                            ParentContainerIds = System.Collections.Immutable.ImmutableArray.Create(CapabilityId.Create(@\"generated:{owner}:harness\")),");
        sb.AppendLine("                            Reveals = System.Collections.Immutable.ImmutableArray.Create<CapabilityId>(");
        for (var i = 0; i < skill.ResolvedFunctionReferences.Count; i++)
        {
            var child = skill.ResolvedFunctionReferences[i];
            var comma = i == skill.ResolvedFunctionReferences.Count - 1 ? string.Empty : ",";
            sb.AppendLine($"                                CapabilityId.Create(@\"generated:{child.Replace("\"", "\"\"")}\"){comma}");
        }
        sb.AppendLine("                            ).AddRange(skillDefinition.Capabilities");
        sb.AppendLine("                                .Where(capability => capability is SkillResource or SkillScript)");
        sb.AppendLine($"                                .Select(capability => CapabilityId.Create(@\"{skillId.Replace("\"", "\"\"")}\" + \":\" + capability.Name)))");
        sb.AppendLine("                        }");
        sb.AppendLine("                        , [SkillRuntimeMetadata.SkillDefinitionKey] = skillDefinition");
        sb.AppendLine("                    }");
        sb.AppendLine("                });");
        sb.AppendLine("        }");

        return sb.ToString();
    }

    /// <summary>
    /// Generates the container function for a collapsed toolharness marked with [ToolHarness("...")].
    /// This groups all functions/skills in the class under a single container.
    /// </summary>
    public static string GenerateToolHarnessContainer(ToolHarnessInfo ToolHarness)
    {
        if (!ToolHarness.IsCollapsed)
            return string.Empty;

        // Must have at least one capability of any type to collapse
        if (!ToolHarness.FunctionCapabilities.Any() && !ToolHarness.SkillCapabilities.Any()
            && !ToolHarness.SubAgentCapabilities.Any() && !ToolHarness.MultiAgentCapabilities.Any()
            && !ToolHarness.MCPServerCapabilities.Any() && !ToolHarness.OpenApiCapabilities.Any())
            return string.Empty;

        var sb = new StringBuilder();

        // Combine all capability types
        var allCapabilities = ToolHarness.FunctionCapabilities.Select(f => f.FunctionName)
            .Concat(ToolHarness.SkillCapabilities.Select(s => s.Name))
            .Concat(ToolHarness.SubAgentCapabilities.Select(s => s.Name))
            .Concat(ToolHarness.MultiAgentCapabilities.Select(m => m.Name))
            .Concat(ToolHarness.MCPServerCapabilities.Select(m => m.Name))
            .Concat(ToolHarness.OpenApiCapabilities.Select(o => o.Prefix ?? o.Name))
            .ToList();
        var capabilitiesList = string.Join(", ", allCapabilities);
        var totalCount = allCapabilities.Count;

        var description = !string.IsNullOrEmpty(ToolHarness.ContainerDescription)
            ? ToolHarness.ContainerDescription
            : ToolHarness.Description ?? string.Empty;

        // Use shared helper to generate description and return message
        // Use EffectiveName for LLM-visible container name
        var fullDescription = ToolHarnessContainerHelper.GenerateContainerDescription(description, ToolHarness.EffectiveName, allCapabilities);
        var returnMessage = ToolHarnessContainerHelper.GenerateReturnMessage(description, allCapabilities, ToolHarness.FunctionResult);

        sb.AppendLine("        /// <summary>");
        sb.AppendLine($"        /// Container function for {ToolHarness.ClassName} toolharness.");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine($"        /// <param name=\"instance\">ToolHarness instance</param>");
        // Method signature uses ClassName for type references
        sb.AppendLine($"        private static AIFunction Create{ToolHarness.ClassName}Container({ToolHarness.ClassName} instance, HPDToolSerializationOptions? serialization)");
        sb.AppendLine("        {");
        sb.AppendLine("            return HPDAIFunctionFactory.Create(");
        sb.AppendLine("                async (arguments, functionContext, cancellationToken) =>");
        sb.AppendLine("                {");

        // Handle FunctionResult - either static literal or dynamic expression
        if (!string.IsNullOrEmpty(ToolHarness.FunctionResultExpression))
        {
            // Using an interpolated string to combine the base message and the dynamic instructions
            var baseMessage = ToolHarnessContainerHelper.GenerateReturnMessage(description, allCapabilities, null);
            // Escape special characters for the interpolated string - we need to convert \n\n to \\n\\n in source code
            baseMessage = baseMessage.Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\"", "\\\"");
            // Add separator between capabilities list and dynamic instructions
            var separator = "\\n\\n";  // This will be two backslash-n sequences in the source code

            // Use instance. prefix for instance methods, nothing for static
            var expressionCall = ToolHarness.FunctionResultIsStatic
                ? ToolHarness.FunctionResultExpression
                : $"instance.{ToolHarness.FunctionResultExpression}";

            sb.AppendLine($"                    var dynamicInstructions = {expressionCall};");
            sb.AppendLine($"                    return $\"{baseMessage}{separator}{{dynamicInstructions}}\";");
        }
        else
        {
            // Using a verbatim string literal for static content
            // In a verbatim string, actual newlines are allowed but we need to represent them as \n
            var escapedReturnMessage = returnMessage
                .Replace("\\", "\\\\")  // Escape backslashes first
                .Replace("\"", "\"\"")  // Escape quotes (double them in verbatim strings)
                .Replace("\n", "\\n"); // Convert actual newlines to backslash-n
            sb.AppendLine($"                    return @\"{escapedReturnMessage}\";");
        }

        sb.AppendLine("                },");
        sb.AppendLine("                new HPDAIFunctionFactoryOptions");
        sb.AppendLine("                {");
        // Use EffectiveName for LLM-visible container function name
        sb.AppendLine($"                    Name = \"{ToolHarness.EffectiveName}\",");
        sb.AppendLine($"                    Description = \"{fullDescription}\",");
        sb.AppendLine("                    SchemaProvider = () => CreateEmptyContainerSchema(),");
        sb.AppendLine("                    SerializerOptions = serialization?.SerializerOptions,");
        sb.AppendLine("                    ResultType = typeof(string),");
        sb.AppendLine("                    AdditionalProperties = new Dictionary<string, object?>");
        sb.AppendLine("                    {");
        var owner = string.IsNullOrEmpty(ToolHarness.Namespace)
            ? ToolHarness.ClassName
            : $"{ToolHarness.Namespace}.{ToolHarness.ClassName}";
        var revealIds = ToolHarness.FunctionCapabilities.Select(function => $"generated:{ToolHarness.ClassName}.{function.FunctionName}")
            .Concat(ToolHarness.SkillCapabilities.Select(skill => $"generated:{owner}:{skill.Name}"))
            .Concat(ToolHarness.SubAgentCapabilities.Select(agent => $"generated:{ToolHarness.ClassName}.{agent.SubAgentName}"))
            .Concat(ToolHarness.MultiAgentCapabilities.Select(agent => $"generated:{ToolHarness.ClassName}.{agent.Name}"))
            .ToArray();
        sb.AppendLine("                        [HPDCapabilityMetadata.AdditionalPropertiesKey] = new HPDCapabilityMetadata");
        sb.AppendLine("                        {");
        sb.AppendLine($"                            Id = CapabilityId.Create(@\"generated:{owner}:harness\"),");
        sb.AppendLine("                            Kind = HPDCapabilityKind.ToolHarnessActivation,");
        sb.AppendLine($"                            Reveals = System.Collections.Immutable.ImmutableArray.Create<CapabilityId>({string.Join(", ", revealIds.Select(id => $"CapabilityId.Create(@\"{id}\")"))})");
        sb.AppendLine("                        },");
        sb.AppendLine("                        [\"IsContainer\"] = true,");
        sb.AppendLine("                        [\"IsToolHarnessContainer\"] = true,");
        sb.AppendLine($"                        [\"ReferencedFunctions\"] = new string[] {{ {string.Join(", ", allCapabilities.Select(c => $"\"{c}\""))} }},");
        sb.AppendLine($"                        [\"FunctionCount\"] = {totalCount},");

        // Add FunctionResult if present
        if (!string.IsNullOrEmpty(ToolHarness.FunctionResult))
        {
            var escapedFuncCtx = ToolHarness.FunctionResult.Replace("\"", "\"\"");
            sb.AppendLine($"                        [\"FunctionResult\"] = @\"{escapedFuncCtx}\",");
        }
        else if (!string.IsNullOrEmpty(ToolHarness.FunctionResultExpression))
        {
            // Expression - evaluate at container creation time
            // Use instance. prefix for instance methods, nothing for static
            var expressionCall = ToolHarness.FunctionResultIsStatic
                ? ToolHarness.FunctionResultExpression
                : $"instance.{ToolHarness.FunctionResultExpression}";

            sb.AppendLine($"                        [\"FunctionResult\"] = {expressionCall},");
        }
        else
        {
            sb.AppendLine("                        [\"FunctionResult\"] = null,");
        }

        // AddSystemPrompt if present
        if (!string.IsNullOrEmpty(ToolHarness.SystemPrompt))
        {
            var escapedSysCtx = ToolHarness.SystemPrompt.Replace("\"", "\"\"");
            sb.AppendLine($"                        [\"SystemPrompt\"] = @\"{escapedSysCtx}\"");
        }
        else if (!string.IsNullOrEmpty(ToolHarness.SystemPromptExpression))
        {
            // Expression - evaluate at container creation time
            // Use instance. prefix for instance methods, nothing for static
            var expressionCall = ToolHarness. SystemPromptIsStatic
                ? ToolHarness.SystemPromptExpression
                : $"instance.{ToolHarness.SystemPromptExpression}";

            sb.AppendLine($"                        [\"SystemPrompt\"] = {expressionCall}");
        }
        else
        {
            sb.AppendLine("                        [\"SystemPrompt\"] = null");
        }

        sb.AppendLine("                    }");
        sb.AppendLine("                });");
        sb.AppendLine("        }");

        return sb.ToString();
    }

    /// <summary>
    /// Generates all skill-related code for a ToolHarness
    /// </summary>
    public static string GenerateAllSkillCode(ToolHarnessInfo ToolHarness)
    {
        // Early exit ONLY if no skills AND not collapsed
        // If ToolHarness is collapsed, we need to generate the container even without skills
        if (!ToolHarness.SkillCapabilities.Any() && !ToolHarness.IsCollapsed)
            return string.Empty;

        var sb = new StringBuilder();

        // Generate toolharness container if collapsed (class-level collapsing)
        if (ToolHarness.IsCollapsed)
        {
            sb.AppendLine(GenerateToolHarnessContainer(ToolHarness));
            sb.AppendLine();
        }

        // Early exit if no skills to generate (but after generating container if needed)
        if (!ToolHarness.SkillCapabilities.Any())
            return sb.ToString();

        // Generate context resolvers for skills (description and conditional)
        foreach (var skill in ToolHarness.SkillCapabilities)
        {
            var resolvers = skill.GenerateContextResolvers();
            if (!string.IsNullOrEmpty(resolvers))
            {
                sb.AppendLine(resolvers);
            }
        }

        // Generate skill functions
        // PHASE 5: Now uses SkillCapabilities
        foreach (var skill in ToolHarness.SkillCapabilities)
        {
            sb.AppendLine();
            // Skills ARE containers - only one function per skill
            sb.AppendLine(GenerateSkillContainerFunction(skill, ToolHarness));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Updates the ToolHarness metadata to include skills
    /// </summary>
    public static string UpdateToolMetadataWithSkills(ToolHarnessInfo ToolHarness, string originalMetadataCode)
    {
        if (!ToolHarness.SkillCapabilities.Any())
            return originalMetadataCode;

        // Add skill information to metadata
        var sb = new StringBuilder();
        sb.AppendLine("        private static ToolMetadata? _cachedMetadata;");
        sb.AppendLine();
        sb.AppendLine("        /// <summary>");
        sb.AppendLine($"        /// Gets metadata for the {ToolHarness.ClassName} ToolHarness (used for Collapsing).");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        public static ToolMetadata GetToolMetadata()");
        sb.AppendLine("        {");
        sb.AppendLine("            return _cachedMetadata ??= new ToolMetadata");
        sb.AppendLine("            {");
        // Use EffectiveName for LLM-visible metadata name
        sb.AppendLine($"                Name = \"{ToolHarness.EffectiveName}\",");

        var description = ToolHarness.IsCollapsed && !string.IsNullOrEmpty(ToolHarness.ContainerDescription)
            ? ToolHarness.ContainerDescription
            : ToolHarness.Description;
        sb.AppendLine($"                Description = \"{description}\",");

        // Include all capability types
        var allFunctionNames = ToolHarness.FunctionCapabilities.Select(f => f.FunctionName)
            .Concat(ToolHarness.SkillCapabilities.Select(s => s.Name))
            .Concat(ToolHarness.SubAgentCapabilities.Select(s => s.Name))
            .Concat(ToolHarness.MultiAgentCapabilities.Select(m => m.Name))
            .Concat(ToolHarness.MCPServerCapabilities.Select(m => m.Name))
            .Concat(ToolHarness.OpenApiCapabilities.Select(o => o.Prefix ?? o.Name))
            .ToList();
        var functionNamesArray = string.Join(", ", allFunctionNames.Select(n => $"\"{n}\""));

        sb.AppendLine($"                FunctionNames = new string[] {{ {functionNamesArray} }},");
        sb.AppendLine($"                FunctionCount = {allFunctionNames.Count},");
        sb.AppendLine($"                IsCollapsed = {ToolHarness.IsCollapsed.ToString().ToLower()}");
        sb.AppendLine("            };");
        sb.AppendLine("        }");

        return sb.ToString();
    }
}
