using HPD.Agent;

namespace HPD.Agent.Tests.TestToolHarnesses;

/// <summary>
/// Test ToolHarness that combines all three capability types: AIFunctions, Skills, and SubAgents.
/// Used to verify the source generator correctly handles ToolHarnesses with mixed capabilities.
/// </summary>
public partial class CombinedCapabilitiesTools
{
    //     
    // AI FUNCTIONS
    //     

    [AIFunction, AIDescription("Analyze data and return insights")]
    public string AnalyzeData(string data) => $"Analysis of: {data}";

    [AIFunction, AIDescription("Transform data into a different format")]
    public string TransformData(string data, string format) => $"Transformed {data} to {format}";

    [AIFunction, AIDescription("Validate data against rules")]
    public bool ValidateData(string data) => !string.IsNullOrEmpty(data);

    //     
    // SKILLS
    //     

    [Skill]
    public static Skill DataAnalysisSkill() => Skill.Create(
        name: "DataAnalysis",
        description: "Comprehensive data analysis workflow",
        instructions: SkillInstructions.FromText("Use AnalyzeData and ValidateData to perform thorough data analysis"),
        capabilities:
        [
            SkillCapabilities.Function<CombinedCapabilitiesTools>(nameof(AnalyzeData)),
            SkillCapabilities.Function<CombinedCapabilitiesTools>(nameof(ValidateData)),
            SkillCapabilities.Resource(
                "read_validation_guide",
                "Reads the validation rules to apply before analysis.",
                "Every record must have a stable identifier.")
        ]);

    [Skill]
    public static Skill DataTransformationSkill() => Skill.Create(
        name: "DataTransformation",
        description: "Data transformation and conversion workflow",
        instructions: SkillInstructions.FromText("Use TransformData to convert data between formats"),
        capabilities:
        [
            SkillCapabilities.Function<CombinedCapabilitiesTools>(nameof(TransformData))
        ]);

    //     
    // SUB-AGENTS
    //     

    [SubAgent]
    public SubAgent DataExpertAgent()
    {
        return SubAgent.FromConfig(
            "test/data-expert",
            "DataExpert",
            "Expert sub-agent specialized in data analysis tasks",
            new AgentConfig
            {
                Name = "Data Expert",
                SystemInstructions = "You are an expert in data analysis. Help users understand their data.",
                MaxAgenticIterations = 10,
                Clients = new AgentClientsConfig { Chat = new ChatClientConfig {
                    ProviderKey = "test",
                    ModelName = "test-model"
                } }
            });
    }

    [SubAgent]
    public SubAgent DataProcessorAgent()
    {
        return SubAgent.FromConfig(
            "test/data-processor",
            "DataProcessor",
            "Sub-agent for batch data processing tasks",
            new AgentConfig
            {
                Name = "Data Processor",
                SystemInstructions = "You process large amounts of data efficiently.",
                MaxAgenticIterations = 20,
                Clients = new AgentClientsConfig { Chat = new ChatClientConfig {
                    ProviderKey = "test",
                    ModelName = "test-model"
                } }
            });
    }
}

/// <summary>
/// ToolHarness with only AIFunctions and SubAgents (no Skills)
/// </summary>
public partial class FunctionsAndSubAgentsToolHarness
{
    // AI Functions
    [AIFunction, AIDescription("Search for items")]
    public string Search(string query) => $"Results for: {query}";

    [AIFunction, AIDescription("Filter results")]
    public string Filter(string results, string criteria) => $"Filtered by {criteria}";

    // Sub-Agent
    [SubAgent]
    public SubAgent SearchExpertAgent()
    {
        return SubAgent.FromConfig(
            "test/search-expert",
            "SearchExpert",
            "Expert in search and discovery",
            new AgentConfig
            {
                Name = "Search Expert",
                SystemInstructions = "You help users find information efficiently.",
                MaxAgenticIterations = 5,
                Clients = new AgentClientsConfig { Chat = new ChatClientConfig {
                    ProviderKey = "test",
                    ModelName = "test-model"
                } }
            });
    }
}

/// <summary>
/// ToolHarness with only Skills and SubAgents (no direct AIFunctions)
/// Note: Skills reference functions from other ToolHarnesses
/// </summary>
public partial class SkillsAndSubAgentsToolHarness
{
    // Skill that references functions from MockFileSystemTools
    [Skill]
    public static Skill FileOperationsSkill() => Skill.Create(
        name: "FileOps",
        description: "File operation workflows",
        instructions: SkillInstructions.FromText("Use file operations for reading and writing"),
        capabilities:
        [
            SkillCapabilities.Function<MockFileSystemTools>(nameof(MockFileSystemTools.ReadFile)),
            SkillCapabilities.Function<MockFileSystemTools>(nameof(MockFileSystemTools.WriteFile))
        ]);

    // Sub-Agent
    [SubAgent]
    public SubAgent FileAssistantAgent()
    {
        return SubAgent.FromConfig(
            "test/file-assistant",
            "FileAssistant",
            "Assistant for file management tasks",
            new AgentConfig
            {
                Name = "File Assistant",
                SystemInstructions = "You help users manage their files.",
                MaxAgenticIterations = 8,
                Clients = new AgentClientsConfig { Chat = new ChatClientConfig {
                    ProviderKey = "test",
                    ModelName = "test-model"
                } }
            });
    }
}
