using System.Collections.Generic;
using System.Linq;
using System.Text;

/// <summary>
/// Generates code for skill registration
/// </summary>
internal static class SkillCodeGenerator
{
    /// <summary>
    /// Generates the GetReferencedHarneses() method for auto-registration
    /// PHASE 5: Now uses SkillCapabilities (fully populated with resolved references)
    /// </summary>
    public static string GenerateGetReferencedHarnesesMethod(HarnessInfo Harness)
    {
        if (!Harness.SkillCapabilities.Any())
            return string.Empty;

        var allReferencedHarneses = Harness.SkillCapabilities
            .SelectMany(s => s.ResolvedHarnessTypes)
            .Distinct()
            .OrderBy(p => p)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// Gets the list of Harneses referenced by skills in this class");
        sb.AppendLine("        /// Used by AgentBuilder for auto-registration");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        public static string[] GetReferencedHarneses()");

        if (!allReferencedHarneses.Any())
        {
            sb.AppendLine("            => Array.Empty<string>();");
            return sb.ToString();
        }
        sb.AppendLine("        {");
        sb.AppendLine("            return new string[]");
        sb.AppendLine("            {");

        for (int i = 0; i < allReferencedHarneses.Count; i++)
        {
            var comma = i < allReferencedHarneses.Count - 1 ? "," : "";
            sb.AppendLine($"                \"{allReferencedHarneses[i]}\"{comma}");
        }

        sb.AppendLine("            };");
        sb.AppendLine("        }");

        return sb.ToString();
    }

    /// <summary>
    /// Generates the GetReferencedFunctions() method for selective function registration
    /// PHASE 5: Now uses SkillCapabilities (fully populated with resolved references)
    /// </summary>
    public static string GenerateGetReferencedFunctionsMethod(HarnessInfo Harness)
    {
        if (!Harness.SkillCapabilities.Any())
            return string.Empty;

        // Build dictionary: HarnessName -> HashSet<FunctionName>
        var toolFunctions = new Dictionary<string, HashSet<string>>();

        foreach (var skill in Harness.SkillCapabilities)
        {
            foreach (var funcRef in skill.ResolvedFunctionReferences)
            {
                // "FileSystemHarness.ReadFile" -> ("FileSystemHarness", "ReadFile")
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
        sb.AppendLine("        /// Used by AgentBuilder to register only needed functions from each Harness");
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
    /// Generates skill registration code to be added to CreateHarness() method
    /// Handles both class-level collapsing (if [Harness] on class with Collapsed=true) and individual skill containers
    /// </summary>
    public static string GenerateSkillRegistrations(HarnessInfo Harness)
    {
        // Early exit ONLY if no skills AND not collapsed
        // If Harness is collapsed, we need to register the container even without skills
        if (!Harness.SkillCapabilities.Any() && !Harness.IsCollapsed)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine();

        // If the Harness is collapsed, create a class-level container first
        if (Harness.IsCollapsed)
        {
            sb.AppendLine("        // Register harness container");
            // Method name uses ClassName; the container's Name property uses EffectiveName
            sb.AppendLine($"        functions.Add(Create{Harness.ClassName}Container(instance, serialization));");
            sb.AppendLine();
        }

        // Early exit if no skills to register (but after registering collapse container if needed)
        if (!Harness.SkillCapabilities.Any())
            return sb.ToString();

        sb.AppendLine("        // Register skill containers");

        foreach (var skill in Harness.SkillCapabilities)
        {
            // Check if skill has conditional registration (same pattern as Functions/SubAgents)
            var hasConditionalEvaluator = skill.IsConditional &&
                                        skill.HasTypedMetadata;

            if (hasConditionalEvaluator)
            {
                sb.AppendLine($"        if (Evaluate{skill.Name}Condition(context))");
                sb.AppendLine("        {");
                sb.AppendLine($"            functions.Add(Create{skill.MethodName}Skill(instance, context, serialization));");
                sb.AppendLine("        }");
            }
            else
            {
                // Each skill generates exactly one container function
                sb.AppendLine($"        functions.Add(Create{skill.MethodName}Skill(instance, context, serialization));");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Generates skill container function.
    /// Skills ARE containers - there's only one function per skill.
    /// PHASE 5: Now accepts SkillCapability instead of SkillInfo
    /// </summary>
    public static string GenerateSkillContainerFunction(HPD.Agent.SourceGenerator.Capabilities.SkillCapability skill, HarnessInfo Harness)
    {
        var sb = new StringBuilder();

        // Simple activation message for function result
        // The prompt Middleware will build the complete context from metadata
        var functionList = string.Join(", ", skill.ResolvedFunctionReferences);
        var returnMessage = $"{skill.Name} skill activated. Available functions: {functionList}";

        // Still include instructions in function result for backward compatibility
        // PHASE 5: SkillCapability uses FunctionResult instead of Instructions
        if (!string.IsNullOrEmpty(skill.FunctionResult))
        {
            returnMessage += $"\n\n{skill.FunctionResult}";
        }

        var escapedReturnMessage = returnMessage.Replace("\"", "\"\"");

        // Build description like Harness Collapsing: append function list
        var functionNames = string.Join(", ", skill.ResolvedFunctionReferences);

        // Support dynamic descriptions (like Functions)
        var descriptionCode = skill.HasDynamicDescription
            ? $"Resolve{skill.Name}Description(context)"
            : $"\"{skill.Description}\"";

        var fullDescriptionTemplate = $"{{0}}. References {skill.ResolvedFunctionReferences.Count} functions: {functionNames}";

        sb.AppendLine($"        /// <summary>");
        sb.AppendLine($"        /// Container function for {skill.Name} skill.");
        sb.AppendLine($"        /// </summary>");
        sb.AppendLine($"        /// <param name=\"instance\">Harness instance</param>");
        sb.AppendLine($"        /// <param name=\"context\">Execution context for dynamic descriptions</param>");
        sb.AppendLine($"        private static AIFunction Create{skill.MethodName}Skill({Harness.Name} instance, IToolMetadata? context, HPDToolSerializationOptions? serialization)");
        sb.AppendLine("        {");

        // Generate runtime function body that checks configuration
        var baseMessage = $"{skill.Name} skill activated. Available functions: {functionList}";
        var escapedBaseMessage = baseMessage.Replace("\"", "\"\"");

        // Determine if skill has documents
        var hasDocuments = skill.Options.DocumentReferences.Any() || skill.Options.DocumentUploads.Any();

        // PHASE 5: SkillCapability uses FunctionResult instead of Instructions
        if (!string.IsNullOrEmpty(skill.FunctionResult))
        {
            var escapedInstructions = skill.FunctionResult.Replace("\"", "\"\"");
            sb.AppendLine("            return HPDAIFunctionFactory.Create(");
            sb.AppendLine("                async (arguments, functionContext, cancellationToken) =>");
            sb.AppendLine("                {");
            sb.AppendLine("                    // Check if instructions should be included in function result");
            sb.AppendLine("                    var mode = HPD.Agent.AgentConfig.GlobalConfig?.Collapsing?.SkillInstructionMode ?? HPD.Agent.SkillInstructionMode.Both;");
            sb.AppendLine("                    if (mode == HPD.Agent.SkillInstructionMode.Both)");
            sb.AppendLine("                    {");
            sb.AppendLine($"                        return @\"{escapedBaseMessage}");
            sb.AppendLine();
            sb.AppendLine($"{escapedInstructions}\";");
            sb.AppendLine("                    }");

            // Generate appropriate message based on whether skill has documents
            if (hasDocuments)
            {
                var documentIds = skill.Options.DocumentUploads.Select(d => d.DocumentId)
                    .Concat(skill.Options.DocumentReferences.Select(r => r.DocumentId))
                    .Distinct().ToList();
                var docPaths = string.Join(", ", documentIds.Select(id => $"content_read(\\\"/skills/{id}\\\")"));
                var documentMessage = $"{skill.Name} skill activated. Available functions: {functionList}.\\n\\nReference documents available in the content store:\\n{string.Join("\\n", documentIds.Select(id => $"- content_read(\\\"/skills/{id}\\\")"))}";
                var escapedDocumentMessage = documentMessage.Replace("\"", "\"\"");
                sb.AppendLine($"                    return @\"{escapedDocumentMessage}\";");
            }
            else
            {
                var reinforcementMessage = $"{skill.Name} skill activated. Available functions: {functionList}.\\n\\nREMINDER: Follow the instructions provided for this skill when using its functions.";
                var escapedReinforcementMessage = reinforcementMessage.Replace("\"", "\"\"");
                sb.AppendLine($"                    return @\"{escapedReinforcementMessage}\";");
            }
            sb.AppendLine("                },");
        }
        else
        {
            sb.AppendLine("            return HPDAIFunctionFactory.Create(");
            sb.AppendLine("                async (arguments, functionContext, cancellationToken) =>");
            sb.AppendLine("                {");

            // Generate appropriate message based on whether skill has documents
            if (hasDocuments)
            {
                var documentIds = skill.Options.DocumentUploads.Select(d => d.DocumentId)
                    .Concat(skill.Options.DocumentReferences.Select(r => r.DocumentId))
                    .Distinct().ToList();
                var documentMessage = $"{skill.Name} skill activated. Available functions: {functionList}.\\n\\nReference documents available in the content store:\\n{string.Join("\\n", documentIds.Select(id => $"- content_read(\\\"/skills/{id}\\\")"))}";
                var escapedDocumentMessage = documentMessage.Replace("\"", "\"\"");
                sb.AppendLine($"                    return @\"{escapedDocumentMessage}\";");
            }
            else
            {
                sb.AppendLine($"                    return @\"{escapedBaseMessage}\";");
            }
            sb.AppendLine("                },");
        }
        sb.AppendLine("                new HPDAIFunctionFactoryOptions");
        sb.AppendLine("                {");
        sb.AppendLine($"                    Name = \"{skill.Name}\",");

        // Use dynamic description if available, otherwise static
        if (skill.HasDynamicDescription)
        {
            // Generate: Description = Resolve{Name}Description(context) + ". References X functions: ..."
            sb.AppendLine($"                    Description = {descriptionCode} + \". References {skill.ResolvedFunctionReferences.Count} functions: {functionNames}\",");
        }
        else
        {
            var staticFullDescription = $"{skill.Description}. References {skill.ResolvedFunctionReferences.Count} functions: {functionNames}";
            sb.AppendLine($"                    Description = \"{staticFullDescription}\",");
        }

        sb.AppendLine($"                    RequiresPermission = {skill.RequiresPermission.ToString().ToLower()},");
        sb.AppendLine("                    SchemaProvider = () => CreateEmptyContainerSchema(),");
        sb.AppendLine("                    SerializerOptions = serialization?.SerializerOptions,");
        sb.AppendLine("                    ResultType = typeof(string),");

        sb.AppendLine("                    AdditionalProperties = new Dictionary<string, object>");
        sb.AppendLine("                    {");
        sb.AppendLine("                        [\"IsContainer\"] = true,");
        sb.AppendLine("                        [\"IsSkill\"] = true,");
        // PHASE 5: SkillCapability uses ParentHarnessName instead of ContainingClass
        sb.AppendLine($"                        [\"ParentContainer\"] = \"{skill.ParentHarnessName}\",");
        sb.AppendLine($"                        [\"ReferencedFunctions\"] = new string[] {{ {string.Join(", ", skill.ResolvedFunctionReferences.Select(f => $"\"{f}\""))} }},");
        sb.AppendLine($"                        [\"ReferencedHarneses\"] = new string[] {{ {string.Join(", ", skill.ResolvedHarnessTypes.Select(p => $"\"{p}\""))} }},");

        // Store instructions separately for prompt Middleware to use
        // Middleware will build complete context from metadata (functions + documents + instructions)

        // NEW: StoreSystemPrompt for middleware injection
        if (!string.IsNullOrEmpty(skill.SystemPrompt))
        {
            var escapedSysPrompt = skill.SystemPrompt.Replace("\"", "\"\"");
            sb.AppendLine($"                        [\"SystemPrompt\"] = @\"{escapedSysPrompt}\",");
        }

        // Store FunctionResult for introspection
        if (!string.IsNullOrEmpty(skill.FunctionResult))
        {
            var escapedFuncResult = skill.FunctionResult.Replace("\"", "\"\"");
            sb.AppendLine($"                        [\"FunctionResult\"] = @\"{escapedFuncResult}\",");
        }

        // LEGACY: Keep Instructions for backward compatibility (auto-maps to both contexts)
        // PHASE 5: SkillCapability uses FunctionResult instead of Instructions
        if (!string.IsNullOrEmpty(skill.FunctionResult))
        {
            var escapedInstructions = skill.FunctionResult.Replace("\"", "\"\"");
            sb.AppendLine($"                        [\"Instructions\"] = @\"{escapedInstructions}\",");
        }

        sb.AppendLine("                    }");
        sb.AppendLine("                });");
        sb.AppendLine("        }");

        return sb.ToString();
    }

    /// <summary>
    /// Generates the container function for a collapsed harness marked with [Harness("...")].
    /// This groups all functions/skills in the class under a single container.
    /// </summary>
    public static string GenerateHarnessContainer(HarnessInfo Harness)
    {
        if (!Harness.IsCollapsed)
            return string.Empty;

        // Must have at least one capability of any type to collapse
        if (!Harness.FunctionCapabilities.Any() && !Harness.SkillCapabilities.Any()
            && !Harness.SubAgentCapabilities.Any() && !Harness.MultiAgentCapabilities.Any()
            && !Harness.MCPServerCapabilities.Any() && !Harness.OpenApiCapabilities.Any())
            return string.Empty;

        var sb = new StringBuilder();

        // Combine all capability types
        var allCapabilities = Harness.FunctionCapabilities.Select(f => f.FunctionName)
            .Concat(Harness.SkillCapabilities.Select(s => s.Name))
            .Concat(Harness.SubAgentCapabilities.Select(s => s.Name))
            .Concat(Harness.MultiAgentCapabilities.Select(m => m.Name))
            .Concat(Harness.MCPServerCapabilities.Select(m => m.Name))
            .Concat(Harness.OpenApiCapabilities.Select(o => o.Prefix ?? o.Name))
            .ToList();
        var capabilitiesList = string.Join(", ", allCapabilities);
        var totalCount = allCapabilities.Count;

        var description = !string.IsNullOrEmpty(Harness.ContainerDescription)
            ? Harness.ContainerDescription
            : Harness.Description ?? string.Empty;

        // Use shared helper to generate description and return message
        // Use EffectiveName for LLM-visible container name
        var fullDescription = HarnessContainerHelper.GenerateContainerDescription(description, Harness.EffectiveName, allCapabilities);
        var returnMessage = HarnessContainerHelper.GenerateReturnMessage(description, allCapabilities, Harness.FunctionResult);

        sb.AppendLine("        /// <summary>");
        sb.AppendLine($"        /// Container function for {Harness.ClassName} harness.");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine($"        /// <param name=\"instance\">Harness instance</param>");
        // Method signature uses ClassName for type references
        sb.AppendLine($"        private static AIFunction Create{Harness.ClassName}Container({Harness.ClassName} instance, HPDToolSerializationOptions? serialization)");
        sb.AppendLine("        {");
        sb.AppendLine("            return HPDAIFunctionFactory.Create(");
        sb.AppendLine("                async (arguments, functionContext, cancellationToken) =>");
        sb.AppendLine("                {");

        // Handle FunctionResult - either static literal or dynamic expression
        if (!string.IsNullOrEmpty(Harness.FunctionResultExpression))
        {
            // Using an interpolated string to combine the base message and the dynamic instructions
            var baseMessage = HarnessContainerHelper.GenerateReturnMessage(description, allCapabilities, null);
            // Escape special characters for the interpolated string - we need to convert \n\n to \\n\\n in source code
            baseMessage = baseMessage.Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\"", "\\\"");
            // Add separator between capabilities list and dynamic instructions
            var separator = "\\n\\n";  // This will be two backslash-n sequences in the source code

            // Use instance. prefix for instance methods, nothing for static
            var expressionCall = Harness.FunctionResultIsStatic
                ? Harness.FunctionResultExpression
                : $"instance.{Harness.FunctionResultExpression}";

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
        sb.AppendLine($"                    Name = \"{Harness.EffectiveName}\",");
        sb.AppendLine($"                    Description = \"{fullDescription}\",");
        sb.AppendLine("                    SchemaProvider = () => CreateEmptyContainerSchema(),");
        sb.AppendLine("                    SerializerOptions = serialization?.SerializerOptions,");
        sb.AppendLine("                    ResultType = typeof(string),");
        sb.AppendLine("                    AdditionalProperties = new Dictionary<string, object?>");
        sb.AppendLine("                    {");
        sb.AppendLine("                        [\"IsContainer\"] = true,");
        sb.AppendLine("                        [\"IsHarnessContainer\"] = true,");
        sb.AppendLine($"                        [\"FunctionNames\"] = new string[] {{ {string.Join(", ", allCapabilities.Select(c => $"\"{c}\""))} }},");
        sb.AppendLine($"                        [\"FunctionCount\"] = {totalCount},");

        // Add FunctionResult if present
        if (!string.IsNullOrEmpty(Harness.FunctionResult))
        {
            var escapedFuncCtx = Harness.FunctionResult.Replace("\"", "\"\"");
            sb.AppendLine($"                        [\"FunctionResult\"] = @\"{escapedFuncCtx}\",");
        }
        else if (!string.IsNullOrEmpty(Harness.FunctionResultExpression))
        {
            // Expression - evaluate at container creation time
            // Use instance. prefix for instance methods, nothing for static
            var expressionCall = Harness.FunctionResultIsStatic
                ? Harness.FunctionResultExpression
                : $"instance.{Harness.FunctionResultExpression}";

            sb.AppendLine($"                        [\"FunctionResult\"] = {expressionCall},");
        }
        else
        {
            sb.AppendLine("                        [\"FunctionResult\"] = null,");
        }

        // AddSystemPrompt if present
        if (!string.IsNullOrEmpty(Harness.SystemPrompt))
        {
            var escapedSysCtx = Harness.SystemPrompt.Replace("\"", "\"\"");
            sb.AppendLine($"                        [\"SystemPrompt\"] = @\"{escapedSysCtx}\"");
        }
        else if (!string.IsNullOrEmpty(Harness.SystemPromptExpression))
        {
            // Expression - evaluate at container creation time
            // Use instance. prefix for instance methods, nothing for static
            var expressionCall = Harness. SystemPromptIsStatic
                ? Harness.SystemPromptExpression
                : $"instance.{Harness.SystemPromptExpression}";

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
    /// Generates all skill-related code for a Harness
    /// </summary>
    public static string GenerateAllSkillCode(HarnessInfo Harness)
    {
        // Early exit ONLY if no skills AND not collapsed
        // If Harness is collapsed, we need to generate the container even without skills
        if (!Harness.SkillCapabilities.Any() && !Harness.IsCollapsed)
            return string.Empty;

        var sb = new StringBuilder();

        // Generate harness container if collapsed (class-level collapsing)
        if (Harness.IsCollapsed)
        {
            sb.AppendLine(GenerateHarnessContainer(Harness));
            sb.AppendLine();
        }

        // Early exit if no skills to generate (but after generating container if needed)
        if (!Harness.SkillCapabilities.Any())
            return sb.ToString();

        // Generate context resolvers for skills (description and conditional)
        foreach (var skill in Harness.SkillCapabilities)
        {
            var resolvers = skill.GenerateContextResolvers();
            if (!string.IsNullOrEmpty(resolvers))
            {
                sb.AppendLine(resolvers);
            }
        }

        // Generate skill functions
        // PHASE 5: Now uses SkillCapabilities
        foreach (var skill in Harness.SkillCapabilities)
        {
            sb.AppendLine();
            // Skills ARE containers - only one function per skill
            sb.AppendLine(GenerateSkillContainerFunction(skill, Harness));
        }

        // Generate InitializeDocumentsAsync if any skill has document uploads or references
        var initDocsCode = GenerateInitializeDocumentsAsync(Harness);
        if (!string.IsNullOrEmpty(initDocsCode))
        {
            sb.AppendLine();
            sb.AppendLine(initDocsCode);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Generates the InitializeDocumentsAsync(IContentStore) method for the registration class.
    /// Called by AgentBuilder.Build() to upload skill documents to the V3 content store at startup.
    /// Only generated when the harness has skills with document uploads or references.
    /// Named upsert semantics: same document ID + same content = no-op (startup-safe).
    /// </summary>
    public static string GenerateInitializeDocumentsAsync(HarnessInfo Harness)
    {
        var allUploads = Harness.SkillCapabilities
            .SelectMany(s => s.Options.DocumentUploads)
            .ToList();
        var allReferences = Harness.SkillCapabilities
            .SelectMany(s => s.Options.DocumentReferences)
            .ToList();

        if (!allUploads.Any() && !allReferences.Any())
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// Uploads skill documents to the V3 content store.");
        sb.AppendLine("        /// Called by AgentBuilder.Build() at startup. Idempotent — safe to call every run.");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        public static async System.Threading.Tasks.Task InitializeDocumentsAsync(HPD.Agent.IContentStore store, System.Threading.CancellationToken cancellationToken = default)");
        sb.AppendLine("        {");

        // Emit uploads (AddDocumentFromFile and AddDocumentFromUrl)
        // Deduplicate by documentId — same doc may appear in multiple skills
        var emittedIds = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var doc in allUploads)
        {
            if (emittedIds.Contains(doc.DocumentId))
                continue;
            emittedIds.Add(doc.DocumentId);

            var escapedDesc = doc.Description.Replace("\"", "\\\"");
            var docId = doc.DocumentId;

            if (doc.SourceType == HPD.Agent.SourceGenerator.Capabilities.DocumentSourceType.FilePath)
            {
                var escapedPath = doc.FilePath!.Replace("\\", "\\\\").Replace("\"", "\\\"");
                sb.AppendLine($"            // From AddDocumentFromFile(\"{escapedPath}\", \"{escapedDesc}\")");
                sb.AppendLine($"            await store.UploadSkillDocumentAsync(");
                sb.AppendLine($"                documentId: \"{docId}\",");
                sb.AppendLine($"                content: await System.IO.File.ReadAllTextAsync(");
                sb.AppendLine($"                    System.IO.Path.IsPathRooted(\"{escapedPath}\")");
                sb.AppendLine($"                        ? \"{escapedPath}\"");
                sb.AppendLine($"                        : System.IO.Path.GetFullPath(\"{escapedPath}\"),");
                sb.AppendLine($"                    cancellationToken),");
                sb.AppendLine($"                description: \"{escapedDesc}\",");
                sb.AppendLine($"                cancellationToken: cancellationToken);");
            }
            else // Url
            {
                var escapedUrl = doc.Url!.Replace("\"", "\\\"");
                sb.AppendLine($"            // From AddDocumentFromUrl(\"{escapedUrl}\", \"{escapedDesc}\")");
                sb.AppendLine($"            {{");
                sb.AppendLine($"                using var __httpClient = new System.Net.Http.HttpClient();");
                sb.AppendLine($"                __httpClient.Timeout = System.TimeSpan.FromSeconds(30);");
                sb.AppendLine($"                var __content = await __httpClient.GetStringAsync(\"{escapedUrl}\", cancellationToken);");
                sb.AppendLine($"                await store.UploadSkillDocumentAsync(");
                sb.AppendLine($"                    documentId: \"{docId}\",");
                sb.AppendLine($"                    content: __content,");
                sb.AppendLine($"                    description: \"{escapedDesc}\",");
                sb.AppendLine($"                    cancellationToken: cancellationToken);");
                sb.AppendLine($"            }}");
            }
            sb.AppendLine();
        }

        // Emit link calls (AddDocument — reference existing documents with per-skill description override)
        foreach (var docRef in allReferences)
        {
            // Find which skill owns this reference for skill name
            var owningSkill = Harness.SkillCapabilities
                .FirstOrDefault(s => s.Options.DocumentReferences.Contains(docRef));
            var skillName = owningSkill?.Name ?? Harness.ClassName;

            var escapedDesc = string.IsNullOrEmpty(docRef.DescriptionOverride)
                ? string.Empty
                : docRef.DescriptionOverride.Replace("\"", "\\\"");

            sb.AppendLine($"            // From AddDocument(\"{docRef.DocumentId}\")");
            sb.AppendLine($"            await store.LinkSkillDocumentAsync(");
            sb.AppendLine($"                documentId: \"{docRef.DocumentId}\",");
            sb.AppendLine($"                skillName: \"{skillName}\",");
            if (!string.IsNullOrEmpty(escapedDesc))
                sb.AppendLine($"                descriptionOverride: \"{escapedDesc}\",");
            else
                sb.AppendLine($"                descriptionOverride: string.Empty,");
            sb.AppendLine($"                cancellationToken: cancellationToken);");
            sb.AppendLine();
        }

        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        /// <summary>True when this harness has skill documents to initialize.</summary>");
        sb.AppendLine("        public static bool HasDocumentsToInitialize => true;");

        return sb.ToString();
    }

    /// <summary>
    /// Derives document ID from file path (matches SkillOptions logic)
    /// </summary>
    private static string DeriveDocumentIdFromPath(string filePath)
    {
        // "./docs/debugging-workflow.md" -> "debugging-workflow"
        var fileName = System.IO.Path.GetFileNameWithoutExtension(filePath);

        // Normalize to lowercase-kebab-case
        return fileName.ToLowerInvariant()
            .Replace(" ", "-")
            .Replace("_", "-");
    }

    /// <summary>
    /// Updates the Harness metadata to include skills
    /// </summary>
    public static string UpdateToolMetadataWithSkills(HarnessInfo Harness, string originalMetadataCode)
    {
        if (!Harness.SkillCapabilities.Any())
            return originalMetadataCode;

        // Add skill information to metadata
        var sb = new StringBuilder();
        sb.AppendLine("        private static ToolMetadata? _cachedMetadata;");
        sb.AppendLine();
        sb.AppendLine("        /// <summary>");
        sb.AppendLine($"        /// Gets metadata for the {Harness.ClassName} Harness (used for Collapsing).");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        public static ToolMetadata GetToolMetadata()");
        sb.AppendLine("        {");
        sb.AppendLine("            return _cachedMetadata ??= new ToolMetadata");
        sb.AppendLine("            {");
        // Use EffectiveName for LLM-visible metadata name
        sb.AppendLine($"                Name = \"{Harness.EffectiveName}\",");

        var description = Harness.IsCollapsed && !string.IsNullOrEmpty(Harness.ContainerDescription)
            ? Harness.ContainerDescription
            : Harness.Description;
        sb.AppendLine($"                Description = \"{description}\",");

        // Include all capability types
        var allFunctionNames = Harness.FunctionCapabilities.Select(f => f.FunctionName)
            .Concat(Harness.SkillCapabilities.Select(s => s.Name))
            .Concat(Harness.SubAgentCapabilities.Select(s => s.Name))
            .Concat(Harness.MultiAgentCapabilities.Select(m => m.Name))
            .Concat(Harness.MCPServerCapabilities.Select(m => m.Name))
            .Concat(Harness.OpenApiCapabilities.Select(o => o.Prefix ?? o.Name))
            .ToList();
        var functionNamesArray = string.Join(", ", allFunctionNames.Select(n => $"\"{n}\""));

        sb.AppendLine($"                FunctionNames = new string[] {{ {functionNamesArray} }},");
        sb.AppendLine($"                FunctionCount = {allFunctionNames.Count},");
        sb.AppendLine($"                IsCollapsed = {Harness.IsCollapsed.ToString().ToLower()}");
        sb.AppendLine("            };");
        sb.AppendLine("        }");

        return sb.ToString();
    }
}
