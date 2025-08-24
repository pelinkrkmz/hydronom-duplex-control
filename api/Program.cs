using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.OpenApi;


var builder = WebApplication.CreateBuilder(args);

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// CORS
builder.Services.AddCors(o =>
{
    o.AddPolicy("dev", p =>
        p.WithOrigins("http://localhost:5173")
         .AllowAnyHeader()
         .AllowAnyMethod()
         .AllowCredentials());
});

// Singletons
builder.Services.AddSingleton<StateStore>();
builder.Services.AddSingleton<WebSocketHub>();
builder.Services.AddSingleton<CommandBus>();

var app = builder.Build();

app.UseCors("dev");
app.UseSwagger();
app.UseSwaggerUI();

// --- Simple auth middleware (dev only)
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/swagger") || ctx.Request.Path.StartsWithSegments("/ws"))
    {
        await next();
        return;
    }
    var ok = ctx.Request.Headers.TryGetValue("Authorization", out var auth)
        && auth.ToString().Contains("DEV_TOKEN");
    if (!ok)
    {
        ctx.Response.StatusCode = 401;
        await ctx.Response.WriteAsync("Unauthorized: use Bearer DEV_TOKEN");
        return;
    }
    await next();
});

// --- Map endpoints

app.MapPost("/api/telemetry", async (HttpContext ctx, StateStore store, WebSocketHub hub) =>
{
    var telemetry = await ctx.Request.ReadFromJsonAsync<TelemetryDto>();
    if (telemetry is null || telemetry.vehicle is null || string.IsNullOrWhiteSpace(telemetry.vehicle.id))
        return Results.BadRequest("Invalid payload");

    store.Upsert(telemetry.vehicle.id, telemetry);
    await store.AppendLogAsync(telemetry);

    // broadcast
    await hub.BroadcastAsync(telemetry.vehicle.id, telemetry);

    return Results.Accepted();
})
.WithOpenApi();

app.MapGet("/api/state/{vehicleId}", (string vehicleId, StateStore store) =>
{
    var state = store.Get(vehicleId);
    return state is null ? Results.NotFound() : Results.Ok(state);
}).WithOpenApi();

app.MapPost("/api/commands", async (CommandDto cmd, CommandBus bus, StateStore store) =>
{
    if (string.IsNullOrWhiteSpace(cmd.vehicle_id)) return Results.BadRequest("vehicle_id required");
    await bus.EnqueueAsync(cmd);
    store.AppendEvent(new EventLog { timestamp = DateTimeOffset.UtcNow, type = "command", vehicleId = cmd.vehicle_id, data = cmd });
    return Results.Ok(new { enqueued = true });
}).WithOpenApi();

// Missions (very minimal in-memory)
app.MapPost("/api/missions", (MissionDto m, StateStore store) =>
{
    store.UpsertMission(m);
    return Results.Ok(m);
}).WithOpenApi();

app.MapGet("/api/missions/{taskId}", (string taskId, StateStore store) =>
{
    var m = store.GetMission(taskId);
    return m is null ? Results.NotFound() : Results.Ok(m);
}).WithOpenApi();

app.MapPost("/api/missions/{taskId}/{action}", (string taskId, string action, StateStore store) =>
{
    var m = store.GetMission(taskId);
    if (m is null) return Results.NotFound();
    store.AppendEvent(new EventLog { timestamp = DateTimeOffset.UtcNow, type = $"mission.{action}", vehicleId = m.vehicle_id ?? "n/a", data = m });
    return Results.Ok(new { ok = true, action });
}).WithOpenApi();

// --- WebSocket endpoint for telemetry stream per vehicle
app.Map("/ws/telemetry/{vehicleId}", async (HttpContext context, string vehicleId, WebSocketHub hub) =>
{
    if (context.WebSockets.IsWebSocketRequest)
    {
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var subId = await hub.AddClientAsync(vehicleId, socket);
        try
        {
            // keep the socket open; we also accept simple ping messages
            var buffer = new byte[1024];
            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), context.RequestAborted);
                if (result.MessageType == WebSocketMessageType.Close) break;
            }
        }
        finally
        {
            await hub.RemoveClientAsync(vehicleId, subId);
        }
    }
    else
    {
        context.Response.StatusCode = 400;
    }
});

// WebSockets
app.UseWebSockets();

app.Run();

// ----------------- Models & Services

record VehicleRef(string id, string type);
record Pose(double lat, double lon, double heading_deg, double speed_mps);
record Imu(double roll_deg, double pitch_deg, double yaw_deg);
record Thrusters(int left_pwm, int right_pwm);
record Ballast(int level_pct);
record Battery(double voltage, double soc_pct);
record MissionState(string mode, string task_id, int waypoint_index);

record TelemetryDto(
    string timestamp,
    VehicleRef vehicle,
    Pose pose,
    double depth_m,
    Imu imu,
    Thrusters thrusters,
    double rudder_deg,
    Ballast ballast,
    Battery battery,
    bool leak,
    double temp_c,
    MissionState mission
);

record CommandDto
{
    public string timestamp { get; init; } = DateTimeOffset.UtcNow.ToString("o");
    public string vehicle_id { get; init; } = "";
    public string command { get; init; } = "";
    public Dictionary<string, object>? payload { get; init; }
}

record MissionDto
{
    public string task_id { get; init; } = "";
    public string name { get; init; } = "";
    public string vehicle_id { get; init; } = "";
    public string mode { get; init; } = "AUTONOMOUS";
    public List<Dictionary<string, object>> waypoints { get; init; } = new();
    public Dictionary<string, object>? constraints { get; init; }
    public string created_by { get; init; } = "operator";
}

class EventLog
{
    public DateTimeOffset timestamp { get; set; }
    public string type { get; set; } = "";
    public string vehicleId { get; set; } = "";
    public object? data { get; set; }
}

class StateStore
{
    private readonly ConcurrentDictionary<string, TelemetryDto> _states = new();
    private readonly ConcurrentDictionary<string, MissionDto> _missions = new();
    private readonly string _logDir;

    public StateStore(IWebHostEnvironment env)
    {
        _logDir = Path.Combine(env.ContentRootPath, "logs");
        Directory.CreateDirectory(_logDir);
    }

    public TelemetryDto? Get(string vehicleId) => _states.TryGetValue(vehicleId, out var t) ? t : default;

    public void Upsert(string vehicleId, TelemetryDto t) => _states[vehicleId] = t;

    public void UpsertMission(MissionDto m) => _missions[m.task_id] = m;

    public MissionDto? GetMission(string taskId) => _missions.TryGetValue(taskId, out var m) ? m : default;

    public void AppendEvent(EventLog e)
    {
        var line = JsonSerializer.Serialize(e);
        var path = Path.Combine(_logDir, $"events-{DateTime.UtcNow:yyyyMMdd}.jsonl");
        File.AppendAllText(path, line + Environment.NewLine);
    }

    public async Task AppendLogAsync(TelemetryDto t)
    {
        var path = Path.Combine(_logDir, $"telemetry-{DateTime.UtcNow:yyyyMMdd}.jsonl");
        var json = JsonSerializer.Serialize(t);
        await File.AppendAllTextAsync(path, json + Environment.NewLine);
    }
}

class WebSocketHub
{
    private class Client { public string Id { get; set; } = Guid.NewGuid().ToString("N"); public WebSocket Socket { get; set; } = default!; }
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, Client>> _clients = new();

    public async Task<string> AddClientAsync(string vehicleId, WebSocket socket)
    {
        var map = _clients.GetOrAdd(vehicleId, _ => new ConcurrentDictionary<string, Client>());
        var c = new Client { Socket = socket };
        map[c.Id] = c;
        // send hello
        var hello = Encoding.UTF8.GetBytes("{\"type\":\"hello\",\"vehicleId\":\"" + vehicleId + "\"}");
        await socket.SendAsync(hello, WebSocketMessageType.Text, true, CancellationToken.None);
        return c.Id;
    }

    public Task RemoveClientAsync(string vehicleId, string clientId)
    {
        if (_clients.TryGetValue(vehicleId, out var map))
            map.TryRemove(clientId, out _);
        return Task.CompletedTask;
    }

    public async Task BroadcastAsync(string vehicleId, TelemetryDto t)
    {
        if (!_clients.TryGetValue(vehicleId, out var map)) return;
        var json = JsonSerializer.Serialize(new { type = "telemetry", data = t });
        var bytes = Encoding.UTF8.GetBytes(json);
        var dead = new List<string>();
        foreach (var kv in map)
        {
            var s = kv.Value.Socket;
            if (s.State == WebSocketState.Open)
            {
                try { await s.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None); }
                catch { dead.Add(kv.Key); }
            }
            else dead.Add(kv.Key);
        }
        foreach (var id in dead) map.TryRemove(id, out _);
    }
}

class CommandBus
{
    private readonly ConcurrentQueue<CommandDto> _q = new();
    public Task EnqueueAsync(CommandDto c) { _q.Enqueue(c); return Task.CompletedTask; }
    public bool TryDequeue(out CommandDto? c) { var ok = _q.TryDequeue(out var x); c = x; return ok; }
}
