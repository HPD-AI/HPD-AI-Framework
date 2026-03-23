using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using HPD.Agent;
namespace HPD.Agent.Tests.Infrastructure;

/// <summary>
/// Custom assertion extensions for common test patterns.
/// Provides more readable and maintainable assertions for agent tests.
/// </summary>
public static class AssertExtensions
{
    /// <summary>
    /// Asserts that two message lists are equal (same count, roles, and content).
    /// </summary>
    public static void EqualMessageLists(
        IEnumerable<ChatMessage> expected,
        IEnumerable<ChatMessage> actual)
    {
        var expectedList = expected.ToList();
        var actualList = actual.ToList();

        Assert.Equal(expectedList.Count, actualList.Count);

        for (int i = 0; i < expectedList.Count; i++)
        {
            var exp = expectedList[i];
            var act = actualList[i];

            Assert.Equal(exp.Role, act.Role);

            // Compare text content
            var expText = string.Join("", exp.Contents.OfType<TextContent>().Select(c => c.Text));
            var actText = string.Join("", act.Contents.OfType<TextContent>().Select(c => c.Text));
            Assert.Equal(expText, actText);

            // Compare function calls
            var expCalls = exp.Contents.OfType<FunctionCallContent>().ToList();
            var actCalls = act.Contents.OfType<FunctionCallContent>().ToList();
            Assert.Equal(expCalls.Count, actCalls.Count);

            for (int j = 0; j < expCalls.Count; j++)
            {
                Assert.Equal(expCalls[j].Name, actCalls[j].Name);
                Assert.Equal(expCalls[j].CallId, actCalls[j].CallId);
            }
        }
    }

    /// <summary>
    /// Asserts that an event of type T exists in the event list, optionally matching a predicate.
    /// </summary>
    public static void ContainsEvent<T>(
        IEnumerable<AgentEvent> events,
        Func<T, bool>? predicate = null)
        where T : AgentEvent
    {
        var matchingEvents = events.OfType<T>();

        if (predicate != null)
        {
            matchingEvents = matchingEvents.Where(predicate);
        }

        Assert.True(
            matchingEvents.Any(),
            $"Expected to find event of type {typeof(T).Name}" +
            (predicate != null ? " matching the predicate" : ""));
    }

    /// <summary>
    /// Asserts that the event sequence matches the expected type sequence exactly.
    /// </summary>
    public static void EventSequenceMatches(
        IEnumerable<Type> expectedTypes,
        IEnumerable<AgentEvent> actualEvents)
    {
        var expectedList = expectedTypes.ToList();
        var actualList = actualEvents.Select(e => e.GetType()).ToList();

        Assert.Equal(expectedList.Count, actualList.Count);

        for (int i = 0; i < expectedList.Count; i++)
        {
            Assert.True(
                expectedList[i].IsAssignableFrom(actualList[i]),
                $"Event at index {i}: expected {expectedList[i].Name}, but got {actualList[i].Name}");
        }
    }

    /// <summary>
    /// Asserts that the event sequence starts with the expected type sequence.
    /// Useful when you only care about the first N events.
    /// </summary>
    public static void EventSequenceStartsWith(
        IEnumerable<Type> expectedTypes,
        IEnumerable<AgentEvent> actualEvents)
    {
        var expectedList = expectedTypes.ToList();
        var actualList = actualEvents.Select(e => e.GetType()).Take(expectedList.Count).ToList();

        Assert.True(
            actualList.Count >= expectedList.Count,
            $"Expected at least {expectedList.Count} events, but got {actualList.Count}");

        for (int i = 0; i < expectedList.Count; i++)
        {
            Assert.True(
                expectedList[i].IsAssignableFrom(actualList[i]),
                $"Event at index {i}: expected {expectedList[i].Name}, but got {actualList[i].Name}");
        }
    }

    /// <summary>
    /// Asserts that events contain the expected sequence in order (but not necessarily consecutively).
    /// </summary>
    public static void EventSequenceContains(
        IEnumerable<Type> expectedTypes,
        IEnumerable<AgentEvent> actualEvents)
    {
        var expectedList = expectedTypes.ToList();
        var actualList = actualEvents.Select(e => e.GetType()).ToList();

        int expectedIndex = 0;
        int actualIndex = 0;

        while (expectedIndex < expectedList.Count && actualIndex < actualList.Count)
        {
            if (expectedList[expectedIndex].IsAssignableFrom(actualList[actualIndex]))
            {
                expectedIndex++;
            }
            actualIndex++;
        }

        Assert.True(
            expectedIndex == expectedList.Count,
            $"Expected to find all {expectedList.Count} event types in sequence, but only found {expectedIndex}");
    }

    /// <summary>
    /// Asserts that exactly N events of type T exist in the event list.
    /// </summary>
    public static void ContainsEventCount<T>(
        IEnumerable<AgentEvent> events,
        int expectedCount,
        Func<T, bool>? predicate = null)
        where T : AgentEvent
    {
        var matchingEvents = events.OfType<T>();

        if (predicate != null)
        {
            matchingEvents = matchingEvents.Where(predicate);
        }

        var actualCount = matchingEvents.Count();
        Assert.Equal(
            expectedCount,
            actualCount);
    }

    /// <summary>
    /// Asserts that no events of type T exist in the event list.
    /// </summary>
    public static void DoesNotContainEvent<T>(
        IEnumerable<AgentEvent> events,
        Func<T, bool>? predicate = null)
        where T : AgentEvent
    {
        var matchingEvents = events.OfType<T>();

        if (predicate != null)
        {
            matchingEvents = matchingEvents.Where(predicate);
        }

        Assert.False(
            matchingEvents.Any(),
            $"Expected NOT to find event of type {typeof(T).Name}" +
            (predicate != null ? " matching the predicate" : ""));
    }

    // NOTE: StateEquals() is intentionally omitted because AgentLoopState doesn't exist yet.
    // This will be added after the refactor when AgentLoopState is introduced.
}
