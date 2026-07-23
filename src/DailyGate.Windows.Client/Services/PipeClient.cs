using System.IO.Pipes;
using System.IO;
using System.Text.Json;
using System.Security.Principal;
using DailyGate.Shared;

namespace DailyGate.Windows.Client.Services;

public sealed class PipeClient
{
    private const string PipeName = "DailyGate.Client";

    public async Task<TResponse> SendAsync<TPayload, TResponse>(string operation, TPayload payload, CancellationToken cancellationToken = default)
    {
        using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous,
            TokenImpersonationLevel.Identification);
        await pipe.ConnectAsync(5000, cancellationToken);
        using var reader = new StreamReader(pipe, leaveOpen: true);
        await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        var request = PipeRequest.Create(operation, payload);
        await writer.WriteLineAsync(JsonSerializer.Serialize(request, JsonDefaults.Options));
        var line = await reader.ReadLineAsync(cancellationToken) ?? throw new IOException("DailyGate service closed the pipe.");
        var response = JsonSerializer.Deserialize<PipeResponse>(line, JsonDefaults.Options)
            ?? throw new IOException("DailyGate service returned an invalid response.");
        if (!response.Success) throw new InvalidOperationException(response.Error ?? "DailyGate service rejected the request.");
        return response.Payload<TResponse>();
    }

    public Task<ClientStatus> StatusAsync(CancellationToken cancellationToken = default)
        => SendAsync<object, ClientStatus>(PipeOperations.Status, new { }, cancellationToken);
}
