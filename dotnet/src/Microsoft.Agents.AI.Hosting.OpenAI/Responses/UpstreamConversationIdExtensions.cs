// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI.Hosting.OpenAI.Responses;

/// <summary>
/// Extension methods for capturing the upstream conversation ID from agent response updates.
/// </summary>
internal static class UpstreamConversationIdExtensions
{
    /// <summary>
    /// Wraps an <see cref="IAsyncEnumerable{AgentResponseUpdate}"/> to capture the upstream
    /// conversation ID (e.g., the Azure Responses API response ID) from the streaming updates
    /// and store it on the <see cref="AgentInvocationContext"/>.
    /// </summary>
    /// <param name="updates">The agent response updates to wrap.</param>
    /// <param name="context">The invocation context where the captured conversation ID will be stored.</param>
    /// <returns>The same stream of updates, with the conversation ID captured as a side effect.</returns>
    public static async IAsyncEnumerable<AgentResponseUpdate> CaptureUpstreamConversationIdAsync(
        this IAsyncEnumerable<AgentResponseUpdate> updates,
        AgentInvocationContext context)
    {
        await foreach (var update in updates.ConfigureAwait(false))
        {
            // The AgentResponseUpdate wraps a ChatResponseUpdate as RawRepresentation.
            // The ChatResponseUpdate.ConversationId is set by MEAI to the upstream service's
            // response ID (e.g., the Azure Responses API response ID), which can be used
            // as previous_response_id in subsequent requests.
            if (update.RawRepresentation is ChatResponseUpdate chatUpdate
                && chatUpdate.ConversationId is not null)
            {
                context.UpstreamConversationId = chatUpdate.ConversationId;
            }

            yield return update;
        }
    }
}
