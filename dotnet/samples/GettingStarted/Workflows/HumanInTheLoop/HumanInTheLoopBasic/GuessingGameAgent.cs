// Copyright (c) Microsoft. All rights reserved.

using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

#pragma warning disable MEAI001 // InputRequestContent/InputResponseContent are evaluation types

namespace WorkflowHumanInTheLoopBasicSample;

/// <summary>
/// A custom <see cref="InputRequestContent"/> subclass that requests a number guess from the user.
/// The MEAI <see cref="InputRequestContent"/> base class has a protected constructor, so applications
/// define their own concrete subclasses for specific input request types (similar to how
/// <see cref="ToolApprovalRequestContent"/> extends it for tool approval scenarios).
/// </summary>
internal sealed class NumberGuessRequestContent : InputRequestContent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NumberGuessRequestContent"/> class.
    /// </summary>
    /// <param name="requestId">The unique identifier that correlates this request with its response.</param>
    /// <param name="hint">A hint message to display to the user.</param>
    [JsonConstructor]
    public NumberGuessRequestContent(string requestId, string hint) : base(requestId)
    {
        this.Hint = hint;
    }

    /// <summary>
    /// Gets the hint message to display to the user.
    /// </summary>
    public string Hint { get; }

    /// <summary>
    /// Creates a <see cref="NumberGuessResponseContent"/> with the user's guess.
    /// </summary>
    /// <param name="guess">The user's guess.</param>
    /// <returns>A response content correlated with this request.</returns>
    public NumberGuessResponseContent CreateResponse(int guess) =>
        new(this.RequestId, guess);
}

/// <summary>
/// A custom <see cref="InputResponseContent"/> subclass that carries the user's number guess.
/// </summary>
internal sealed class NumberGuessResponseContent : InputResponseContent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NumberGuessResponseContent"/> class.
    /// </summary>
    /// <param name="requestId">The unique identifier that correlates this response with its request.</param>
    /// <param name="guess">The user's guess.</param>
    [JsonConstructor]
    public NumberGuessResponseContent(string requestId, int guess) : base(requestId)
    {
        this.Guess = guess;
    }

    /// <summary>
    /// Gets the user's guess.
    /// </summary>
    public int Guess { get; }
}

/// <summary>
/// A simple AIAgent implementation that plays a number guessing game using MEAI
/// <see cref="InputRequestContent"/> and <see cref="InputResponseContent"/> types
/// for human-in-the-loop interaction.
/// </summary>
/// <remarks>
/// When hosted in a workflow via <see cref="Microsoft.Agents.AI.Workflows.AIAgentBinding"/>,
/// the <c>AIAgentHostExecutor</c> automatically detects <see cref="InputRequestContent"/>
/// subclasses in the agent's response and routes them to the external consumer as a
/// <see cref="Microsoft.Agents.AI.Workflows.RequestInfoEvent"/>.
/// </remarks>
internal sealed class GuessingGameAgent : AIAgent
{
    private readonly int _targetNumber;

    /// <summary>
    /// Initializes a new instance of the <see cref="GuessingGameAgent"/> class.
    /// </summary>
    /// <param name="targetNumber">The number to be guessed.</param>
    public GuessingGameAgent(int targetNumber)
    {
        this._targetNumber = targetNumber;
    }

    /// <inheritdoc/>
    protected override string? IdCore => "GuessingGameAgent";

    /// <inheritdoc/>
    public override string? Name => "GuessingGameAgent";

    /// <inheritdoc/>
    protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default)
        => new(new GuessingGameSession());

    /// <inheritdoc/>
    protected override ValueTask<JsonElement> SerializeSessionCoreAsync(AgentSession session, JsonSerializerOptions? jsonSerializerOptions = null, CancellationToken cancellationToken = default)
        => new(JsonSerializer.SerializeToElement((GuessingGameSession)session, jsonSerializerOptions));

    /// <inheritdoc/>
    protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(JsonElement serializedState, JsonSerializerOptions? jsonSerializerOptions = null, CancellationToken cancellationToken = default)
        => new(JsonSerializer.Deserialize<GuessingGameSession>(serializedState, jsonSerializerOptions)
            ?? throw new JsonException("Failed to deserialize session."));

    /// <inheritdoc/>
    protected override Task<AgentResponse> RunCoreAsync(IEnumerable<ChatMessage> messages, AgentSession? session = null, AgentRunOptions? options = null, CancellationToken cancellationToken = default)
        => this.RunCoreStreamingAsync(messages, session, options, cancellationToken).ToAgentResponseAsync(cancellationToken);

    /// <inheritdoc/>
    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        session ??= await this.CreateSessionAsync(cancellationToken);
        GuessingGameSession gameSession = (GuessingGameSession)session;

        // Check if the user has responded with a NumberGuessResponseContent
        NumberGuessResponseContent? guessResponse = messages
            .SelectMany(m => m.Contents)
            .OfType<NumberGuessResponseContent>()
            .FirstOrDefault();

        if (guessResponse is not null)
        {
            gameSession.Tries++;
            int guess = guessResponse.Guess;

            if (guess == this._targetNumber)
            {
                yield return new(ChatRole.Assistant, $"Correct! {this._targetNumber} found in {gameSession.Tries} tries!");
                yield break;
            }

            // Not correct - emit feedback and a new NumberGuessRequestContent for the next guess
            string hint = guess < this._targetNumber
                ? $"Your guess of {guess} is too small. Try again."
                : $"Your guess of {guess} is too large. Try again.";

            yield return new(ChatRole.Assistant, hint);
            yield return new(ChatRole.Assistant, [new NumberGuessRequestContent(Guid.NewGuid().ToString("N"), hint)]);
            yield break;
        }

        // First invocation: emit a NumberGuessRequestContent to ask for the initial guess
        const string initialHint = "I'm thinking of a number. Can you guess it?";
        yield return new(ChatRole.Assistant, initialHint);
        yield return new(ChatRole.Assistant, [new NumberGuessRequestContent(Guid.NewGuid().ToString("N"), initialHint)]);
    }

    /// <summary>
    /// Session state for the guessing game.
    /// </summary>
    private sealed class GuessingGameSession : AgentSession
    {
        public int Tries { get; set; }
    }
}
