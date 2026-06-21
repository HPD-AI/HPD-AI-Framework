// Copyright (c) 2025 Einstein Essibu. All rights reserved.

using HPD.Agent;
using HPD.Agent.Middleware;
using HPD.Agent.Middleware.Image;
using Microsoft.Extensions.AI;
using Xunit;

namespace HPD.Agent.Tests.Middleware;

public class ImageMiddlewareTests
{
    [Fact]
    public void Constructor_WithNullStrategy_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ImageMiddleware(null!, new ImageMiddlewareOptions()));
    }

    [Fact]
    public void Constructor_WithNullOptions_UsesDefaultOptions()
    {
        // Arrange
        var strategy = new PassThroughImageStrategy();

        // Act
        var middleware = new ImageMiddleware(strategy, options: null);

        // Assert - No exception, middleware created successfully
        Assert.NotNull(middleware);
    }

    [Fact]
    public void AgentBuilderExtension_WithImageHandling_AddsMiddleware()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        var result = builder.WithImageHandling();

        // Assert - Should return builder for chaining
        Assert.Same(builder, result);
    }

    [Fact]
    public void AgentBuilderExtension_WithCustomOptions_UsesPassThroughByDefault()
    {
        // Arrange
        var builder = new AgentBuilder();
        var options = new ImageMiddlewareOptions
        {
            ProcessingStrategy = ImageProcessingStrategy.PassThrough
        };

        // Act
        var result = builder.WithImageHandling(options);

        // Assert - Should return builder for chaining
        Assert.Same(builder, result);
    }

    [Fact]
    public void AgentBuilderExtension_WithOcrStrategy_ThrowsNotImplementedException()
    {
        // Arrange
        var builder = new AgentBuilder();
        var options = new ImageMiddlewareOptions
        {
            ProcessingStrategy = ImageProcessingStrategy.OCR
        };

        // Act & Assert
        Assert.Throws<NotImplementedException>(() =>
            builder.WithImageHandling(options));
    }

    [Fact]
    public void AgentBuilderExtension_WithDescriptionStrategy_ThrowsNotImplementedException()
    {
        // Arrange
        var builder = new AgentBuilder();
        var options = new ImageMiddlewareOptions
        {
            ProcessingStrategy = ImageProcessingStrategy.Description
        };

        // Act & Assert
        Assert.Throws<NotImplementedException>(() =>
            builder.WithImageHandling(options));
    }

    [Fact]
    public void AgentBuilderExtension_WithUnknownStrategy_ThrowsArgumentException()
    {
        // Arrange
        var builder = new AgentBuilder();
        var options = new ImageMiddlewareOptions
        {
            ProcessingStrategy = "UnknownStrategy"
        };

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            builder.WithImageHandling(options));
    }

    [Fact]
    public void AgentBuilderExtension_WithCustomStrategy_AddsMiddleware()
    {
        // Arrange
        var builder = new AgentBuilder();
        var customStrategy = new PassThroughImageStrategy();

        // Act
        var result = builder.WithImageHandling(customStrategy);

        // Assert - Should return builder for chaining
        Assert.Same(builder, result);
    }

    [Fact]
    public void ImageMiddlewareOptions_DefaultStrategy_IsPassThrough()
    {
        // Arrange & Act
        var options = new ImageMiddlewareOptions();

        // Assert
        Assert.Equal(ImageProcessingStrategy.PassThrough, options.ProcessingStrategy);
    }

    [Fact]
    public void ImageProcessingStrategy_Constants_AreCorrect()
    {
        // Assert
        Assert.Equal("PassThrough", ImageProcessingStrategy.PassThrough);
        Assert.Equal("OCR", ImageProcessingStrategy.OCR);
        Assert.Equal("Description", ImageProcessingStrategy.Description);
    }
}
