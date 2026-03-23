using HPD.Agent;
using HPD.MultiAgent;
using HPD.MultiAgent.Config;

namespace HPD.MultiAgent.Tests;

public class AgentWorkflowBuilderTests
{
    [Fact]
    public void Create_Returns_New_Builder()
    {
        var builder = AgentWorkflow.Create();

        builder.Should().NotBeNull();
        builder.Should().BeOfType<MultiAgent>();
    }

    [Fact]
    public void WithName_Sets_Workflow_Name()
    {
        var builder = AgentWorkflow.Create()
            .WithName("TestWorkflow");

        builder.Should().NotBeNull();
    }

    [Fact]
    public void AddAgent_With_Config_Stores_Config()
    {
        var config = new AgentConfig
        {
            Name = "TestAgent",
            SystemInstructions = "You are a test agent"
        };

        var builder = AgentWorkflow.Create()
            .AddAgent("test", config);

        builder.Should().NotBeNull();
    }

    [Fact]
    public void AddAgent_With_Options_Applies_Options()
    {
        var config = new AgentConfig { Name = "Test", SystemInstructions = "Test" };

        var builder = AgentWorkflow.Create()
            .AddAgent("test", config, o =>
            {
                o.WithTimeout(TimeSpan.FromSeconds(30));
                o.WithRetry(3);
            });

        builder.Should().NotBeNull();
    }

    [Fact]
    public void AddAgent_With_Empty_Id_Throws()
    {
        var config = new AgentConfig { Name = "Test" };

        Action act = () => AgentWorkflow.Create()
            .AddAgent("", config);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddAgent_With_Null_Config_Throws()
    {
        Action act = () => AgentWorkflow.Create()
            .AddAgent("test", (AgentConfig)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void From_Returns_EdgeBuilder()
    {
        var config = new AgentConfig { Name = "Test", SystemInstructions = "Test" };

        var edgeBuilder = AgentWorkflow.Create()
            .AddAgent("test", config)
            .From("START");

        edgeBuilder.Should().NotBeNull();
    }

    [Fact]
    public void From_With_Multiple_Sources()
    {
        var config = new AgentConfig { Name = "Test", SystemInstructions = "Test" };

        var edgeBuilder = AgentWorkflow.Create()
            .AddAgent("solver1", config)
            .AddAgent("solver2", config)
            .From("solver1", "solver2");

        edgeBuilder.Should().NotBeNull();
    }

    [Fact]
    public void From_With_Empty_Sources_Throws()
    {
        Action act = () => AgentWorkflow.Create()
            .From();

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void WithMaxIterations_Sets_Max_Iterations()
    {
        var builder = AgentWorkflow.Create()
            .WithMaxIterations(10);

        builder.Should().NotBeNull();
    }

    [Fact]
    public void WithTimeout_Sets_Timeout()
    {
        var builder = AgentWorkflow.Create()
            .WithTimeout(TimeSpan.FromMinutes(5));

        builder.Should().NotBeNull();
    }

    [Fact]
    public void FromConfig_With_Valid_Config_Creates_Builder()
    {
        var config = new MultiAgentWorkflowConfig
        {
            Name = "TestWorkflow",
            Agents = new Dictionary<string, AgentNodeConfig>
            {
                ["classifier"] = new AgentNodeConfig
                {
                    Agent = new AgentConfig
                    {
                        Name = "Classifier",
                        SystemInstructions = "Classify queries"
                    }
                }
            },
            Edges = new List<EdgeConfig>
            {
                new EdgeConfig { From = "START", To = "classifier" },
                new EdgeConfig { From = "classifier", To = "END" }
            }
        };

        var builder = AgentWorkflow.FromConfig(config);

        builder.Should().NotBeNull();
    }

    [Fact]
    public void Chained_Edge_Definitions_Work()
    {
        var config = new AgentConfig { Name = "Test", SystemInstructions = "Test" };

        var builder = AgentWorkflow.Create()
            .WithName("ChainedEdges")
            .AddAgent("classifier", config)
            .AddAgent("solver", config)
            .AddAgent("general", config)
            .From("START").To("classifier")
            .From("classifier").To("solver").WhenEquals("is_math", true)
            .From("classifier").To("general").WhenEquals("is_math", false)
            .From("solver", "general").To("END");

        builder.Should().NotBeNull();
    }
}
