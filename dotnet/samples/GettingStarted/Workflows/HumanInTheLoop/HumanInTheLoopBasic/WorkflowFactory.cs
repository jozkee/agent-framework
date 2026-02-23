// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;

namespace WorkflowHumanInTheLoopBasicSample;

internal static class WorkflowFactory
{
    /// <summary>
    /// Get a workflow that plays a number guessing game with human-in-the-loop interaction
    /// using a low-level RequestPort.
    /// An input port allows the external world to provide inputs to the workflow upon requests.
    /// </summary>
    internal static Workflow BuildWorkflow()
    {
        // Create the executors
        RequestPort numberRequestPort = RequestPort.Create<NumberSignal, int>("GuessNumber");
        JudgeExecutor judgeExecutor = new(42);

        // Build the workflow by connecting executors in a loop
        return new WorkflowBuilder(numberRequestPort)
            .AddEdge(numberRequestPort, judgeExecutor)
            .AddEdge(judgeExecutor, numberRequestPort)
            .WithOutputFrom(judgeExecutor)
            .Build();
    }

    /// <summary>
    /// Get a workflow that plays a number guessing game with human-in-the-loop interaction
    /// using MEAI <see cref="Microsoft.Extensions.AI.InputRequestContent"/> /
    /// <see cref="Microsoft.Extensions.AI.InputResponseContent"/> types.
    /// The <see cref="GuessingGameAgent"/> is hosted as a workflow executor via
    /// <see cref="AIAgentBinding"/>, and the <c>AIAgentHostExecutor</c> automatically
    /// routes <see cref="Microsoft.Extensions.AI.InputRequestContent"/> from the agent's
    /// response to the external consumer.
    /// </summary>
    internal static Workflow BuildMeaiWorkflow()
    {
        // Create an AIAgent that uses InputRequestContent/InputResponseContent for HIL
        GuessingGameAgent agent = new(targetNumber: 42);

        // Bind the agent as a workflow executor - the AIAgentHostExecutor will automatically
        // detect InputRequestContent in the agent's response and route it externally.
        // EmitAgentResponseEvents enables us to observe the agent's text responses in the stream.
        ExecutorBinding agentExecutor = agent.BindAsExecutor(new AIAgentHostOptions
        {
            EmitAgentResponseEvents = true
        });

        // Build a simple single-executor workflow
        return new WorkflowBuilder(agentExecutor)
            .Build();
    }
}

/// <summary>
/// Signals used for communication between guesses and the JudgeExecutor.
/// </summary>
internal enum NumberSignal
{
    Init,
    Above,
    Below,
}

/// <summary>
/// Executor that judges the guess and provides feedback.
/// </summary>
internal sealed class JudgeExecutor() : Executor<int>("Judge")
{
    private readonly int _targetNumber;
    private int _tries;

    /// <summary>
    /// Initializes a new instance of the <see cref="JudgeExecutor"/> class.
    /// </summary>
    /// <param name="targetNumber">The number to be guessed.</param>
    public JudgeExecutor(int targetNumber) : this()
    {
        this._targetNumber = targetNumber;
    }

    public override async ValueTask HandleAsync(int message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        this._tries++;
        if (message == this._targetNumber)
        {
            await context.YieldOutputAsync($"{this._targetNumber} found in {this._tries} tries!", cancellationToken);
        }
        else if (message < this._targetNumber)
        {
            await context.SendMessageAsync(NumberSignal.Below, cancellationToken: cancellationToken);
        }
        else
        {
            await context.SendMessageAsync(NumberSignal.Above, cancellationToken: cancellationToken);
        }
    }
}
