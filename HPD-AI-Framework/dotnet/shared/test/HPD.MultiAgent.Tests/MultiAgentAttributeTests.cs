using HPD.Agent;

namespace HPD.MultiAgent.Tests;

/// <summary>
/// Tests for the [MultiAgent] attribute used to mark toolkit methods
/// that return multi-agent workflows.
/// </summary>
public class MultiAgentAttributeTests
{
    [Fact]
    public void MultiAgentAttribute_DefaultConstructor_HasDefaultValues()
    {
        // Act
        var attr = new MultiAgentAttribute();

        // Assert
        attr.Description.Should().BeNull();
        attr.Name.Should().BeNull();
        attr.StreamEvents.Should().BeTrue();
        attr.TimeoutSeconds.Should().Be(300);
    }

    [Fact]
    public void MultiAgentAttribute_WithDescription_SetsDescription()
    {
        // Act
        var attr = new MultiAgentAttribute("Parallel analysis workflow");

        // Assert
        attr.Description.Should().Be("Parallel analysis workflow");
        attr.StreamEvents.Should().BeTrue();
        attr.TimeoutSeconds.Should().Be(300);
    }

    [Fact]
    public void MultiAgentAttribute_NameProperty_CanBeSet()
    {
        // Act
        var attr = new MultiAgentAttribute("Description")
        {
            Name = "CustomWorkflowName"
        };

        // Assert
        attr.Name.Should().Be("CustomWorkflowName");
        attr.Description.Should().Be("Description");
    }

    [Fact]
    public void MultiAgentAttribute_StreamEventsProperty_CanBeDisabled()
    {
        // Act
        var attr = new MultiAgentAttribute
        {
            StreamEvents = false
        };

        // Assert
        attr.StreamEvents.Should().BeFalse();
    }

    [Fact]
    public void MultiAgentAttribute_TimeoutSecondsProperty_CanBeCustomized()
    {
        // Act
        var attr = new MultiAgentAttribute
        {
            TimeoutSeconds = 600
        };

        // Assert
        attr.TimeoutSeconds.Should().Be(600);
    }

    [Fact]
    public void MultiAgentAttribute_AllPropertiesCanBeSet()
    {
        // Act
        var attr = new MultiAgentAttribute("Full config workflow")
        {
            Name = "FullConfigWorkflow",
            StreamEvents = false,
            TimeoutSeconds = 120
        };

        // Assert
        attr.Description.Should().Be("Full config workflow");
        attr.Name.Should().Be("FullConfigWorkflow");
        attr.StreamEvents.Should().BeFalse();
        attr.TimeoutSeconds.Should().Be(120);
    }

    [Fact]
    public void MultiAgentAttribute_CanBeAppliedToMethod()
    {
        // Arrange - Get a method with [MultiAgent] attribute
        var methodInfo = typeof(SampleToolkit).GetMethod(nameof(SampleToolkit.TestWorkflow));

        // Act
        var attrs = methodInfo?.GetCustomAttributes(typeof(MultiAgentAttribute), false);

        // Assert
        attrs.Should().NotBeNull();
        attrs.Should().HaveCount(1);
        var attr = attrs![0] as MultiAgentAttribute;
        attr.Should().NotBeNull();
        attr!.Description.Should().Be("Test multi-agent workflow");
    }

    [Fact]
    public void MultiAgentAttribute_InheritsFromAttribute()
    {
        // Act
        var attr = new MultiAgentAttribute();

        // Assert
        attr.Should().BeAssignableTo<Attribute>();
    }

    // Sample toolkit class for testing attribute application
    private class SampleToolkit
    {
        [MultiAgent("Test multi-agent workflow")]
        public AgentWorkflowInstance TestWorkflow()
        {
            // This would normally return a real workflow
            // For testing attribute detection, we don't need a real implementation
            throw new NotImplementedException("Test method for attribute detection");
        }

        [MultiAgent("Configured workflow", Name = "CustomName", StreamEvents = false, TimeoutSeconds = 60)]
        public AgentWorkflowInstance ConfiguredWorkflow()
        {
            throw new NotImplementedException("Test method for attribute detection");
        }
    }
}

/// <summary>
/// Tests for the generic [MultiAgent&lt;TMetadata&gt;] attribute
/// used for context-aware multi-agent workflows.
/// </summary>
public class GenericMultiAgentAttributeTests
{
    [Fact]
    public void MultiAgentAttribute_Generic_HasContextType()
    {
        // Act
        var attr = new MultiAgentAttribute<TestMetadata>();

        // Assert
        attr.ContextType.Should().Be(typeof(TestMetadata));
    }

    [Fact]
    public void MultiAgentAttribute_Generic_WithDescription()
    {
        // Act
        var attr = new MultiAgentAttribute<TestMetadata>("Context-aware workflow");

        // Assert
        attr.Description.Should().Be("Context-aware workflow");
        attr.ContextType.Should().Be(typeof(TestMetadata));
    }

    [Fact]
    public void MultiAgentAttribute_Generic_AllPropertiesWork()
    {
        // Act
        var attr = new MultiAgentAttribute<TestMetadata>("Full config")
        {
            Name = "ContextWorkflow",
            StreamEvents = false,
            TimeoutSeconds = 180
        };

        // Assert
        attr.Description.Should().Be("Full config");
        attr.Name.Should().Be("ContextWorkflow");
        attr.StreamEvents.Should().BeFalse();
        attr.TimeoutSeconds.Should().Be(180);
        attr.ContextType.Should().Be(typeof(TestMetadata));
    }

    // Test metadata class
    private class TestMetadata : IToolMetadata
    {
        public bool EnableAdvanced { get; set; }
        public string Tier { get; set; } = "standard";

        public T? GetProperty<T>(string propertyName, T? defaultValue = default)
        {
            return propertyName switch
            {
                nameof(EnableAdvanced) => (T)(object)EnableAdvanced,
                nameof(Tier) => (T)(object)Tier!,
                _ => defaultValue
            };
        }

        public bool HasProperty(string propertyName)
        {
            return propertyName is nameof(EnableAdvanced) or nameof(Tier);
        }

        public IEnumerable<string> GetPropertyNames()
        {
            yield return nameof(EnableAdvanced);
            yield return nameof(Tier);
        }
    }
}
