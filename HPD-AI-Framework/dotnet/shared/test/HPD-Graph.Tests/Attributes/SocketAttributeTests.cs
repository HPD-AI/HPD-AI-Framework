using FluentAssertions;
using HPDAgent.Graph.Abstractions.Attributes;
using System.Reflection;
using Xunit;

namespace HPD.Graph.Tests.Attributes;

/// <summary>
/// Tests for Socket attributes used by the source generator.
/// Note: Full source generator tests require a separate Roslyn-based test project.
/// These tests verify the attributes themselves are properly defined.
/// </summary>
public class SocketAttributeTests
{
    #region InputSocketAttribute Tests

    [Fact]
    public void InputSocketAttribute_CanBeAppliedToParameters()
    {
        // Arrange & Act
        var attribute = typeof(InputSocketAttribute).GetCustomAttribute<AttributeUsageAttribute>();

        // Assert
        attribute.Should().NotBeNull();
        attribute!.ValidOn.Should().HaveFlag(AttributeTargets.Parameter);
    }

    [Fact]
    public void InputSocketAttribute_OptionalProperty_DefaultsFalse()
    {
        // Arrange & Act
        var attr = new InputSocketAttribute();

        // Assert
        attr.Optional.Should().BeFalse();
    }

    [Fact]
    public void InputSocketAttribute_OptionalProperty_CanBeSet()
    {
        // Arrange & Act
        var attr = new InputSocketAttribute { Optional = true };

        // Assert
        attr.Optional.Should().BeTrue();
    }

    [Fact]
    public void InputSocketAttribute_DescriptionProperty_CanBeSet()
    {
        // Arrange & Act
        var attr = new InputSocketAttribute { Description = "Test input description" };

        // Assert
        attr.Description.Should().Be("Test input description");
    }

    [Fact]
    public void InputSocketAttribute_DescriptionProperty_DefaultsNull()
    {
        // Arrange & Act
        var attr = new InputSocketAttribute();

        // Assert
        attr.Description.Should().BeNull();
    }

    [Fact]
    public void InputSocketAttribute_BothProperties_CanBeSet()
    {
        // Arrange & Act
        var attr = new InputSocketAttribute
        {
            Optional = true,
            Description = "Optional input"
        };

        // Assert
        attr.Optional.Should().BeTrue();
        attr.Description.Should().Be("Optional input");
    }

    #endregion

    #region OutputSocketAttribute Tests

    [Fact]
    public void OutputSocketAttribute_CanBeAppliedToProperties()
    {
        // Arrange & Act
        var attribute = typeof(OutputSocketAttribute).GetCustomAttribute<AttributeUsageAttribute>();

        // Assert
        attribute.Should().NotBeNull();
        attribute!.ValidOn.Should().HaveFlag(AttributeTargets.Property);
    }

    [Fact]
    public void OutputSocketAttribute_DescriptionProperty_CanBeSet()
    {
        // Arrange & Act
        var attr = new OutputSocketAttribute { Description = "Test output description" };

        // Assert
        attr.Description.Should().Be("Test output description");
    }

    [Fact]
    public void OutputSocketAttribute_DescriptionProperty_DefaultsNull()
    {
        // Arrange & Act
        var attr = new OutputSocketAttribute();

        // Assert
        attr.Description.Should().BeNull();
    }

    #endregion

    #region GraphNodeHandlerAttribute Tests

    [Fact]
    public void GraphNodeHandlerAttribute_CanBeAppliedToClasses()
    {
        // Arrange & Act
        var attribute = typeof(GraphNodeHandlerAttribute).GetCustomAttribute<AttributeUsageAttribute>();

        // Assert
        attribute.Should().NotBeNull();
        attribute!.ValidOn.Should().HaveFlag(AttributeTargets.Class);
    }

    [Fact]
    public void GraphNodeHandlerAttribute_NodeNameProperty_CanBeSet()
    {
        // Arrange & Act
        var attr = new GraphNodeHandlerAttribute { NodeName = "MyCustomHandler" };

        // Assert
        attr.NodeName.Should().Be("MyCustomHandler");
    }

    [Fact]
    public void GraphNodeHandlerAttribute_NodeNameProperty_DefaultsNull()
    {
        // Arrange & Act
        var attr = new GraphNodeHandlerAttribute();

        // Assert
        attr.NodeName.Should().BeNull();
    }

    #endregion

    #region Integration: Attribute Application Tests

    [Fact]
    public void TestMethod_WithInputSocketAttribute_CanBeReflected()
    {
        // Arrange
        var method = typeof(SampleHandler).GetMethod(nameof(SampleHandler.Execute));
        var parameters = method!.GetParameters();
        var inputParam = parameters.First(p => p.Name == "input");

        // Act
        var attr = inputParam.GetCustomAttribute<InputSocketAttribute>();

        // Assert
        attr.Should().NotBeNull();
        attr!.Optional.Should().BeFalse();
        attr.Description.Should().Be("Sample input parameter");
    }

    [Fact]
    public void TestClass_WithGraphNodeHandlerAttribute_CanBeReflected()
    {
        // Arrange & Act
        var attr = typeof(SampleHandler).GetCustomAttribute<GraphNodeHandlerAttribute>();

        // Assert
        attr.Should().NotBeNull();
        attr!.NodeName.Should().Be("SampleHandler");
    }

    [Fact]
    public void TestProperty_WithOutputSocketAttribute_CanBeReflected()
    {
        // Arrange
        var property = typeof(SampleHandlerOutput).GetProperty(nameof(SampleHandlerOutput.Result));

        // Act
        var attr = property!.GetCustomAttribute<OutputSocketAttribute>();

        // Assert
        attr.Should().NotBeNull();
        attr!.Description.Should().Be("Sample output result");
    }

    #endregion

    #region Test Classes for Reflection

    [GraphNodeHandler(NodeName = "SampleHandler")]
    private class SampleHandler
    {
        public SampleHandlerOutput Execute(
            [InputSocket(Optional = false, Description = "Sample input parameter")]
            string input)
        {
            return new SampleHandlerOutput { Result = input };
        }
    }

    private class SampleHandlerOutput
    {
        [OutputSocket(Description = "Sample output result")]
        public string Result { get; set; } = "";
    }

    #endregion
}
