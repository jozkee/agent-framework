// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Agents.AI.Hosting.OpenAI.Responses.Models;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI.Hosting.OpenAI.Responses.Streaming;

/// <summary>
/// A generator for streaming events from function approval request content.
/// This is a non-standard DevUI extension for human-in-the-loop scenarios.
/// </summary>
internal sealed class FunctionApprovalRequestEventGenerator(
        IdGenerator idGenerator,
        SequenceNumber seq,
        int outputIndex,
        JsonSerializerOptions jsonSerializerOptions) : StreamingEventGenerator
{
    public override bool IsSupported(AIContent content) => content is ToolApprovalRequestContent;

    public override IEnumerable<StreamingResponseEvent> ProcessContent(AIContent content)
    {
        if (content is not ToolApprovalRequestContent approvalRequest)
        {
            throw new InvalidOperationException("FunctionApprovalRequestEventGenerator only supports ToolApprovalRequestContent.");
        }

        var (callId, name, arguments) = approvalRequest.ToolCall switch
        {
            FunctionCallContent fcc => (fcc.CallId, fcc.Name, fcc.Arguments),
            McpServerToolCallContent mcp => (mcp.CallId, mcp.Name, mcp.Arguments),
            _ => throw new InvalidOperationException($"Unsupported tool call type: {approvalRequest.ToolCall?.GetType().Name}")
        };

        yield return new StreamingFunctionApprovalRequested
        {
            SequenceNumber = seq.Increment(),
            OutputIndex = outputIndex,
            RequestId = approvalRequest.RequestId,
            ItemId = idGenerator.GenerateMessageId(),
            FunctionCall = new FunctionCallInfo
            {
                Id = callId,
                Name = name,
                Arguments = JsonSerializer.SerializeToElement(
                    arguments,
                    jsonSerializerOptions.GetTypeInfo(typeof(IDictionary<string, object>)))
            }
        };
    }

    public override IEnumerable<StreamingResponseEvent> Complete() => [];
}
