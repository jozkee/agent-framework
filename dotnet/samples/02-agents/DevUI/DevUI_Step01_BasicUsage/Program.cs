// Copyright (c) Microsoft. All rights reserved.

// This sample demonstrates basic usage of the DevUI in an ASP.NET Core application with AI agents.

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using OpenAI.Responses;

namespace DevUI_Step01_BasicUsage;

/// <summary>
/// Sample demonstrating basic usage of the DevUI in an ASP.NET Core application.
/// </summary>
/// <remarks>
/// This sample shows how to:
/// 1. Set up Azure OpenAI as the chat client
/// 2. Create function tools for agents to use
/// 3. Add a remote MCP server tool with approval via HostedMcpServerTool
/// 4. Register agents and workflows using the hosting packages with tools
/// 5. Map the DevUI endpoint which automatically configures the middleware
/// 6. Map the dynamic OpenAI Responses API for Python DevUI compatibility
/// 7. Access the DevUI in a web browser
///
/// The DevUI provides an interactive web interface for testing and debugging AI agents.
/// DevUI assets are served from embedded resources within the assembly.
/// Simply call MapDevUI() to set up everything needed.
///
/// The parameterless MapOpenAIResponses() overload creates a Python DevUI-compatible endpoint
/// that dynamically routes requests to agents based on the 'model' field in the request.
/// </remarks>
internal static class Program
{
    /// <summary>
    /// Entry point that starts an ASP.NET Core web server with the DevUI.
    /// </summary>
    /// <param name="args">Command line arguments.</param>
    private static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Set up the Azure OpenAI client
        var endpoint = builder.Configuration["AZURE_OPENAI_ENDPOINT"] ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not set.");
        var deploymentName = builder.Configuration["AZURE_OPENAI_DEPLOYMENT_NAME"] ?? "gpt-4o-mini";

        // WARNING: DefaultAzureCredential is convenient for development but requires careful consideration in production.
        // In production, consider using a specific credential (e.g., ManagedIdentityCredential) to avoid
        // latency issues, unintended credential probing, and potential security risks from fallback mechanisms.
        var azureOpenAIClient = new AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential());

        // Register IChatClient for agents that don't need the Responses API
        var chatClient = azureOpenAIClient
            .GetChatClient(deploymentName)
            .AsIChatClient();
        builder.Services.AddChatClient(chatClient);

        // Create a HostedMcpServerTool for the Microsoft Learn documentation search.
        // HostedMcpServerTool requires the Responses API — the service handles MCP calls server-side.
        var mcpTool = new HostedMcpServerTool(
            serverName: "microsoft_learn",
            serverAddress: "https://learn.microsoft.com/api/mcp")
        {
            AllowedTools = ["microsoft_docs_search"],
            ApprovalMode = HostedMcpServerToolApprovalMode.AlwaysRequire
        };

        // Define some example tools
        [Description("Get the weather for a given location.")]
        static string GetWeather([Description("The location to get the weather for.")] string location)
        {
            // Debugger.Launch();
            if (!location.Equals("Austin", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    throw new ArgumentException("QWEWQEWQE");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Stack: {ex.StackTrace}");
                    throw;
                }
            }

            return $"The weather in {location} is cloudy with a high of 15°C.";
        }

        [Description("Calculate the sum of two numbers.")]
        static double Add([Description("The first number.")] double a, [Description("The second number.")] double b)
            => a + b;

        [Description("Get the current time.")]
        static string GetCurrentTime()
            => DateTime.Now.ToString("HH:mm:ss");

        // Register the assistant agent using the Responses API (required for HostedMcpServerTool)
        // Build the IChatClient pipeline explicitly so we can log on both sides of FIC:
        //   MeaiLoggingChatClient("outer") → FIC → MeaiLoggingChatClient("inner") → MEAI adapter → Azure
        var responsesClient = azureOpenAIClient.GetResponsesClient();
        var agentTools = new List<AITool>
        {
            new ApprovalRequiredAIFunction(AIFunctionFactory.Create(GetWeather, name: "get_weather")),
            AIFunctionFactory.Create(GetCurrentTime, name: "get_current_time"),
            mcpTool
        };

        builder.Services.AddAIAgent("assistant", (sp, key) =>
        {
            var pipeline = responsesClient
                .AsIChatClient(deploymentName)
                .AsBuilder()
                .Use(inner => new MeaiLoggingChatClient(inner, "inner"))
                .UseFunctionInvocation()
                .Use(inner => new MeaiLoggingChatClient(inner, "outer"))
                .Build();

            return new ChatClientAgent(
                pipeline,
                new ChatClientAgentOptions
                {
                    Name = key,
                    UseProvidedChatClientAsIs = true,
                    ChatOptions = new ChatOptions
                    {
                        Instructions = "You are a helpful assistant. Answer questions concisely and accurately. Use Microsoft Learn MCP tools to search documentation when asked about Microsoft products.",
                        Tools = agentTools
                    }
                });
        });

        builder.AddAIAgent("poet", "You are a creative poet. Respond to all requests with beautiful poetry.");

        builder.AddAIAgent("coder", "You are an expert programmer. Help users with coding questions and provide code examples.")
            .WithAITool(AIFunctionFactory.Create(Add, name: "add"));

        // Register sample workflows
        var assistantBuilder = builder.AddAIAgent("workflow-assistant", "You are a helpful assistant in a workflow.");
        var reviewerBuilder = builder.AddAIAgent("workflow-reviewer", "You are a reviewer. Review and critique the previous response.");
        builder.AddWorkflow("review-workflow", (sp, key) =>
        {
            var agents = new List<IHostedAgentBuilder>() { assistantBuilder, reviewerBuilder }.Select(ab => sp.GetRequiredKeyedService<AIAgent>(ab.Name));
            return AgentWorkflowBuilder.BuildSequential(workflowName: key, agents: agents);
        }).AddAsAIAgent();

        builder.Services.AddOpenAIResponses();
        builder.Services.AddOpenAIConversations();

        var app = builder.Build();

        // Log request/response bodies for the Responses API to diagnose MCP approval flow
        var logFile = Path.Combine(AppContext.BaseDirectory, "responses-api.log");
        var meaiLogFile = Path.Combine(AppContext.BaseDirectory, "meai-pipeline.log");
        Console.WriteLine($"API log file: {logFile}");
        Console.WriteLine($"MEAI log file: {meaiLogFile}");
        var logWriter = new StreamWriter(logFile, append: false) { AutoFlush = true };
        MeaiLoggingChatClient.Initialize(meaiLogFile);

        app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments("/v1/responses"))
            {
                await next();
                return;
            }

            // Read and log the request body
            context.Request.EnableBuffering();
            using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
            var requestBody = await reader.ReadToEndAsync();
            context.Request.Body.Position = 0;
            var header = $"\n===== REQUEST {context.Request.Method} {context.Request.Path} [{DateTime.Now:HH:mm:ss}] =====";
            Console.WriteLine(header);
            logWriter.WriteLine(header);
            logWriter.WriteLine(requestBody);

            // Wrap response stream to log SSE events as they pass through without buffering
            var originalBody = context.Response.Body;
            context.Response.Body = new LoggingStream(originalBody, logWriter);

            await next();

            context.Response.Body = originalBody;
        });

        app.MapOpenAIResponses();
        app.MapOpenAIConversations();

        if (builder.Environment.IsDevelopment())
        {
            app.MapDevUI();
        }

        Console.WriteLine("DevUI is available at: https://localhost:50516/devui");
        Console.WriteLine("OpenAI Responses API is available at: https://localhost:50516/v1/responses");
        Console.WriteLine("Press Ctrl+C to stop the server.");

        app.Run();
    }
}

/// <summary>
/// Pass-through stream wrapper that logs written data to the console without buffering.
/// </summary>
internal sealed class LoggingStream(Stream inner, StreamWriter logWriter) : Stream
{
    public override bool CanRead => inner.CanRead;

    public override bool CanSeek => inner.CanSeek;

    public override bool CanWrite => inner.CanWrite;

    public override long Length => inner.Length;

    public override long Position { get => inner.Position; set => inner.Position = value; }

    public override void Flush() => inner.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

    public override void SetLength(long value) => inner.SetLength(value);

    public override void Write(byte[] buffer, int offset, int count)
    {
        logWriter.Write(System.Text.Encoding.UTF8.GetString(buffer, offset, count));
        inner.Write(buffer, offset, count);
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        logWriter.Write(System.Text.Encoding.UTF8.GetString(buffer.Span));
        await inner.WriteAsync(buffer, cancellationToken);
    }
}

/// <summary>
/// Delegating chat client that logs the ChatMessage objects flowing through the MEAI pipeline.
/// Use the label parameter to distinguish multiple instances (e.g., "outer" above FIC, "inner" below FIC).
/// </summary>
internal sealed class MeaiLoggingChatClient(IChatClient innerClient, string label = "") : DelegatingChatClient(innerClient)
{
    private static StreamWriter? s_writer;

    public static void Initialize(string logFilePath)
    {
        s_writer = new StreamWriter(logFilePath, append: false) { AutoFlush = true };
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        LogMessages($"GetResponseAsync [{label}]", messages);
        var response = await base.GetResponseAsync(messages, options, cancellationToken);
        LogResponse(response);
        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        LogMessages($"GetStreamingResponseAsync [{label}]", messages);

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            updates.Add(update);
            yield return update;
        }

        var response = updates.ToChatResponse();
        LogResponse(response);
    }

    private static void Log(string line)
    {
        Console.WriteLine(line);
        s_writer?.WriteLine(line);
    }

    private static void LogMessages(string method, IEnumerable<ChatMessage> messages)
    {
        Log($"\n===== MEAI {method} [{DateTime.Now:HH:mm:ss}] =====");

        foreach (var msg in messages)
        {
            Log($"  [{msg.Role}] contents: {msg.Contents.Count}");
            foreach (var content in msg.Contents)
            {
                switch (content)
                {
                    case TextContent text:
                        Log($"    TextContent: {Truncate(text.Text, 120)}");
                        break;
                    case FunctionCallContent fcc:
                        Log($"    FunctionCallContent: id={fcc.CallId} name={fcc.Name} args={Serialize(fcc.Arguments)}");
                        break;
                    case FunctionResultContent frc:
                        Log($"    FunctionResultContent: id={frc.CallId} result={Truncate(frc.Result?.ToString(), 120)}");
                        break;
                    case ToolApprovalRequestContent tar:
                        Log($"    ToolApprovalRequestContent: requestId={tar.RequestId} toolCall={tar.ToolCall?.GetType().Name} ({DescribeToolCall(tar.ToolCall)})");
                        break;
                    case ToolApprovalResponseContent tares:
                        Log($"    ToolApprovalResponseContent: requestId={tares.RequestId} approved={tares.Approved} toolCall={tares.ToolCall?.GetType().Name}");
                        break;
                    default:
                        Log($"    {content.GetType().Name}");
                        break;
                }
            }
        }
    }

    private static void LogResponse(ChatResponse response)
    {
        Log($"  --- response: {response.Messages.Count} message(s) ---");
        foreach (var msg in response.Messages)
        {
            Log($"  [{msg.Role}] contents: {msg.Contents.Count}");
            foreach (var content in msg.Contents)
            {
                switch (content)
                {
                    case TextContent text:
                        Log($"    TextContent: {Truncate(text.Text, 200)}");
                        break;
                    case FunctionCallContent fcc:
                        Log($"    FunctionCallContent: id={fcc.CallId} name={fcc.Name} args={Serialize(fcc.Arguments)}");
                        break;
                    case FunctionResultContent frc:
                        Log($"    FunctionResultContent: id={frc.CallId} result={Truncate(frc.Result?.ToString(), 200)}");
                        break;
                    case ToolApprovalRequestContent tar:
                        Log($"    ToolApprovalRequestContent: requestId={tar.RequestId} ({DescribeToolCall(tar.ToolCall)})");
                        break;
                    case ToolApprovalResponseContent tares:
                        Log($"    ToolApprovalResponseContent: requestId={tares.RequestId} approved={tares.Approved}");
                        break;
                    case UsageContent:
                        break;
                    default:
                        Log($"    {content.GetType().Name}: {Truncate(content.ToString(), 200)}");
                        break;
                }
            }
        }
    }

    private static string DescribeToolCall(AIContent? toolCall) => toolCall switch
    {
        FunctionCallContent fcc => $"FCC name={fcc.Name} id={fcc.CallId}",
        McpServerToolCallContent mcp => $"MCP server={mcp.ServerName} name={mcp.Name} id={mcp.CallId}",
        _ => toolCall?.GetType().Name ?? "null"
    };

    private static string? Truncate(string? s, int max) =>
        s is null ? null : s.Length <= max ? s : s[..max] + "...";

    private static string? Serialize(IDictionary<string, object?>? dict) =>
        dict is null ? null : JsonSerializer.Serialize(dict);
}
