// Copyright (c) Microsoft. All rights reserved.

using System.Linq;
using Microsoft.Agents.AI.Workflows.Declarative.Events;
using Microsoft.Extensions.AI;
using Xunit.Abstractions;

namespace Microsoft.Agents.AI.Workflows.Declarative.UnitTests.Events;

/// <summary>
/// Verify <see cref="ExternalInputRequest"/> class
/// </summary>
public sealed class ExternalInputRequestTest(ITestOutputHelper output) : EventTest(output)
{
    [Fact]
    public void VerifySerializationWithText()
    {
        // Arrange
        ExternalInputRequest source = new(new AgentResponse(new ChatMessage(ChatRole.User, "Wassup?")));

        // Act
        ExternalInputRequest copy = VerifyEventSerialization(source);

        // Assert
        ChatMessage messageCopy = Assert.Single(source.AgentResponse.Messages);
        AssertMessage(messageCopy, copy.AgentResponse.Messages[0]);
    }

    [Fact]
    public void VerifySerializationWithRequests()
    {
        // Arrange
        ExternalInputRequest source =
            new(new AgentResponse(
                    new ChatMessage(
                        ChatRole.Assistant,
                        [
                            new FunctionApprovalRequestContent("call1", new McpServerToolCallContent("call1", "testmcp", "server-name")),
                            new FunctionApprovalRequestContent("call2", new FunctionCallContent("call2", "result1")),
                            new FunctionCallContent("call3", "myfunc"),
                            new TextContent("Heya"),
                        ])));

        // Act
        ExternalInputRequest copy = VerifyEventSerialization(source);

        // Assert
        ChatMessage messageCopy = Assert.Single(source.AgentResponse.Messages);
        Assert.Equal(messageCopy.Contents.Count, copy.AgentResponse.Messages[0].Contents.Count);

        var approvalRequests = messageCopy.Contents.OfType<FunctionApprovalRequestContent>().ToArray();
        Assert.Equal(2, approvalRequests.Length);

        FunctionApprovalRequestContent mcpRequest = approvalRequests.Single(x => x.RequestId == "call1");
        Assert.NotNull(mcpRequest.FunctionCall);

        FunctionApprovalRequestContent functionRequest = approvalRequests.Single(x => x.RequestId == "call2");
        Assert.NotNull(functionRequest.FunctionCall);

        FunctionCallContent functionCall = AssertContent<FunctionCallContent>(messageCopy);
        Assert.Equal("call3", functionCall.CallId);

        TextContent textContent = AssertContent<TextContent>(messageCopy);
        Assert.Equal("Heya", textContent.Text);
    }
}
