// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace WorkflowHumanInTheLoopBasicSample;

/// <summary>
/// This sample introduces the concept of RequestPort and ExternalRequest to enable
/// human-in-the-loop interaction scenarios.
/// A request port can be used as if it were an executor in the workflow graph. Upon receiving
/// a message, the request port generates an RequestInfoEvent that gets emitted to the external world.
/// The external world can then respond to the request by sending an ExternalResponse back to
/// the workflow.
/// The sample implements a simple number guessing game where the external user tries to guess
/// a pre-defined target number. The workflow consists of a single JudgeExecutor that judges
/// the user's guesses and provides feedback.
///
/// The sample also demonstrates the MEAI-based approach where an AIAgent produces
/// InputRequestContent subclasses in its response. When hosted via AIAgentBinding,
/// the AIAgentHostExecutor automatically detects these content types and routes them as
/// RequestInfoEvent to the external consumer. The external consumer responds
/// with an InputResponseContent subclass to continue the conversation.
/// </summary>
/// <remarks>
/// Pre-requisites:
/// - Foundational samples should be completed first.
/// </remarks>
public static class Program
{
    private static async Task Main()
    {
        // Console.WriteLine("=== Demo 1: RequestPort-based Human-in-the-Loop ===");
        // Console.WriteLine();
        // await RunRequestPortDemoAsync();

        Console.WriteLine();
        Console.WriteLine("=== Demo 2: MEAI InputRequestContent-based Human-in-the-Loop ===");
        Console.WriteLine();
        await RunMeaiDemoAsync();
    }

    /// <summary>
    /// Demonstrates HIL using the low-level RequestPort approach with custom signal types.
    /// </summary>
    private static async Task RunRequestPortDemoAsync()
    {
        // Create the workflow
        var workflow = WorkflowFactory.BuildWorkflow();

        // Execute the workflow
        await using StreamingRun handle = await InProcessExecution.StreamAsync(workflow, NumberSignal.Init);
        await foreach (WorkflowEvent evt in handle.WatchStreamAsync())
        {
            switch (evt)
            {
                case RequestInfoEvent requestInputEvt:
                    // Handle `RequestInfoEvent` from the workflow
                    ExternalResponse response = HandleExternalRequest(requestInputEvt.Request);
                    await handle.SendResponseAsync(response);
                    break;

                case WorkflowOutputEvent outputEvt:
                    // The workflow has yielded output
                    Console.WriteLine($"Workflow completed with result: {outputEvt.Data}");
                    return;
            }
        }
    }

    /// <summary>
    /// Demonstrates HIL using MEAI InputRequestContent and InputResponseContent
    /// types. The <see cref="GuessingGameAgent"/> produces <see cref="NumberGuessRequestContent"/> in its
    /// response, which the <c>AIAgentHostExecutor</c> automatically routes to the external consumer.
    /// </summary>
    private static async Task RunMeaiDemoAsync()
    {
        // Build a workflow that hosts an AIAgent using AIAgentBinding
        var workflow = WorkflowFactory.BuildMeaiWorkflow();

        // Execute the workflow using Lockstep mode with a ChatMessage.
        // The AIAgentHostExecutor requires a TurnToken to start processing.
        List<ChatMessage> messages = [new(ChatRole.User, "start")];
        await using StreamingRun handle = await InProcessExecution.Lockstep.StreamAsync(workflow, messages);
        await handle.TrySendMessageAsync(new TurnToken());

        await foreach (WorkflowEvent evt in handle.WatchStreamAsync())
        {
            switch (evt)
            {
                case RequestInfoEvent requestInfoEvt:
                    // The AIAgentHostExecutor has detected InputRequestContent in the agent's response
                    // and routed it as an ExternalRequest. We check for our concrete subclass.
                    if (requestInfoEvt.Request.DataIs(out NumberGuessRequestContent? guessRequest))
                    {
                        // Prompt the user for a number guess
                        int guess = ReadIntegerFromConsole("Enter your guess: ");

                        // Create a typed response using the request's factory method
                        NumberGuessResponseContent guessResponse = guessRequest.CreateResponse(guess);

                        // Send the response back to the workflow
                        await handle.SendResponseAsync(
                            requestInfoEvt.Request.CreateResponse(guessResponse));
                    }

                    break;

                case AgentResponseEvent responseEvt:
                    // The agent has produced a response. Print the text if present.
                    if (!string.IsNullOrEmpty(responseEvt.Response.Text))
                    {
                        Console.WriteLine($"Agent: {responseEvt.Response.Text}");
                    }

                    break;
            }
        }

        Console.WriteLine("Workflow completed.");
    }

    private static ExternalResponse HandleExternalRequest(ExternalRequest request)
    {
        if (request.DataIs<NumberSignal>())
        {
            switch (request.DataAs<NumberSignal>())
            {
                case NumberSignal.Init:
                    int initialGuess = ReadIntegerFromConsole("Please provide your initial guess: ");
                    return request.CreateResponse(initialGuess);
                case NumberSignal.Above:
                    int lowerGuess = ReadIntegerFromConsole("You previously guessed too large. Please provide a new guess: ");
                    return request.CreateResponse(lowerGuess);
                case NumberSignal.Below:
                    int higherGuess = ReadIntegerFromConsole("You previously guessed too small. Please provide a new guess: ");
                    return request.CreateResponse(higherGuess);
            }
        }

        throw new NotSupportedException($"Request {request.PortInfo.RequestType} is not supported");
    }

    private static int ReadIntegerFromConsole(string prompt)
    {
        while (true)
        {
            Console.Write(prompt);
            string? input = Console.ReadLine();
            if (int.TryParse(input, out int value))
            {
                return value;
            }
            Console.WriteLine("Invalid input. Please enter a valid integer.");
        }
    }
}
