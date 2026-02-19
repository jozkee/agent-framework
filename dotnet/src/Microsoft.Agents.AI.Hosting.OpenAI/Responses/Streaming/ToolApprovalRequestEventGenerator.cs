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
internal sealed class ToolApprovalRequestEventGenerator(
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
            throw new InvalidOperationException("ToolApprovalRequestEventGenerator only supports ToolApprovalRequestContent.");
        }

        ToolCallInfo toolCallInfo = approvalRequest.ToolCall switch
        {
            McpServerToolCallContent mcp => new McpToolCallInfo
            {
                Id = mcp.CallId,
                Name = mcp.Name,
                Arguments = JsonSerializer.SerializeToElement(
                    mcp.Arguments,
                    jsonSerializerOptions.GetTypeInfo(typeof(IDictionary<string, object>))),
                ServerName = mcp.ServerName!
            },
            FunctionCallContent fcc => new FunctionToolCallInfo
            {
                Id = fcc.CallId,
                Name = fcc.Name,
                Arguments = JsonSerializer.SerializeToElement(
                    fcc.Arguments,
                    jsonSerializerOptions.GetTypeInfo(typeof(IDictionary<string, object>)))
            },
            _ => throw new InvalidOperationException($"Unsupported tool call type: {approvalRequest.ToolCall?.GetType().Name}")
        };

        yield return new StreamingToolApprovalRequested
        {
            SequenceNumber = seq.Increment(),
            OutputIndex = outputIndex,
            RequestId = approvalRequest.RequestId,
            ItemId = idGenerator.GenerateMessageId(),
            ToolCall = toolCallInfo
        };
    }

    public override IEnumerable<StreamingResponseEvent> Complete() => [];
}
