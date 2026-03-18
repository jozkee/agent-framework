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

        var functionCallInfo = approvalRequest.ToolCall switch
        {
            FunctionCallContent fcc => new FunctionCallInfo
            {
                Id = fcc.CallId,
                Name = fcc.Name,
                Arguments = fcc.Arguments is not null
                    ? JsonSerializer.SerializeToElement(
                        fcc.Arguments,
                        jsonSerializerOptions.GetTypeInfo(typeof(IDictionary<string, object>)))
                    : default
            },
            McpServerToolCallContent mcc => new FunctionCallInfo
            {
                Id = mcc.CallId,
                Name = mcc.Name,
                ServerLabel = mcc.ServerName,
                Arguments = mcc.Arguments is not null
                    ? JsonSerializer.SerializeToElement(
                        mcc.Arguments,
                        jsonSerializerOptions.GetTypeInfo(typeof(IDictionary<string, object>)))
                    : default
            },
            _ => new FunctionCallInfo
            {
                Id = approvalRequest.ToolCall.CallId,
                Name = approvalRequest.ToolCall.CallId,
                Arguments = default
            }
        };

        // Build the ItemResource for standard storage path
        string? serializedArgs = functionCallInfo.Arguments.ValueKind != JsonValueKind.Undefined
            ? functionCallInfo.Arguments.GetRawText() : null;

        ItemResource item = functionCallInfo.ServerLabel is not null
            ? new MCPApprovalRequestItemResource
            {
                Id = approvalRequest.RequestId,
                ServerLabel = functionCallInfo.ServerLabel,
                Name = functionCallInfo.Name,
                Arguments = serializedArgs
            }
            : new FCCApprovalRequestItemResource
            {
                Id = approvalRequest.RequestId,
                CallId = functionCallInfo.Id,
                Name = functionCallInfo.Name,
                Arguments = serializedArgs
            };

        // Emit standard output item events so storage picks these up automatically
        yield return new StreamingOutputItemAdded
        {
            SequenceNumber = seq.Increment(),
            OutputIndex = outputIndex,
            Item = item
        };

        // Emit the custom DevUI event for the frontend approval dialog
        yield return new StreamingFunctionApprovalRequested
        {
            SequenceNumber = seq.Increment(),
            OutputIndex = outputIndex,
            RequestId = approvalRequest.RequestId,
            ItemId = item.Id,
            FunctionCall = functionCallInfo
        };

        yield return new StreamingOutputItemDone
        {
            SequenceNumber = seq.Increment(),
            OutputIndex = outputIndex,
            Item = item
        };
    }

    public override IEnumerable<StreamingResponseEvent> Complete() => [];
}
