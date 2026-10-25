using System.Collections.Concurrent;
using System.Threading.Channels;
using PegasusDeck.Core.Agent;
using PegasusDeck.Core.Contract;

namespace PegasusDeck.Service.Agent;

/// <summary>
/// Cola de lo que hay que enviar a la PWA por WebSocket (P-2): cambios de estado
/// (se agrupan y se envía una instantánea) y resultados de operaciones.
/// </summary>
public sealed class ServiceEvents
{
    /// <summary>Marca "el estado ha cambiado": EventsHub envía una instantánea nueva.</summary>
    public sealed class SnapshotDirty
    {
        public static SnapshotDirty Instance { get; } = new();
    }

    private readonly Channel<object> _out = Channel.CreateBounded<object>(
        new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    public ChannelReader<object> Reader => _out.Reader;

    public void SnapshotChanged() => _out.Writer.TryWrite(SnapshotDirty.Instance);

    public void Publish(OperationMessage message) => _out.Writer.TryWrite(message);
}

/// <summary>
/// Operaciones aceptadas pendientes de confirmación. El servicio es dueño del
/// operationId y del tiempo máximo; si el agente se cae, las pendientes fallan.
/// </summary>
public sealed class OperationTracker(ServiceEvents events, TimeProvider time) : IDisposable
{
    private static readonly HashSet<string> KnownCodes =
        typeof(ErrorCodes).GetFields().Select(f => (string)f.GetValue(null)!).ToHashSet(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, ITimer> _pending = new(StringComparer.Ordinal);

    public int Pending => _pending.Count;

    public void Track(string operationId, TimeSpan timeout)
    {
        var timer = time.CreateTimer(_ => Expire(operationId), null, timeout, Timeout.InfiniteTimeSpan);
        if (!_pending.TryAdd(operationId, timer)) timer.Dispose();
    }

    public bool IsTracked(string operationId) => _pending.ContainsKey(operationId);

    /// <summary>La acción terminó de forma inmediata (completed/failed): no hay nada que confirmar.</summary>
    public void Forget(string operationId)
    {
        if (_pending.TryRemove(operationId, out var t)) t.Dispose();
    }

    /// <summary>Confirmación del agente. Solo se publica si la operación estaba pendiente.</summary>
    public void Complete(AgentOperation op)
    {
        if (!_pending.TryRemove(op.OperationId, out var t)) return;
        t.Dispose();
        var ok = op.Status == "completed";
        var code = ok ? null : (op.Code is not null && KnownCodes.Contains(op.Code) ? op.Code : ErrorCodes.Internal);
        events.Publish(new OperationMessage(new OperationResult(op.OperationId, ok ? "completed" : "failed", code, Clip(op.Message))));
    }

    public void FailAll(string code, string message)
    {
        foreach (var id in _pending.Keys)
        {
            if (!_pending.TryRemove(id, out var t)) continue;
            t.Dispose();
            events.Publish(new OperationMessage(new OperationResult(id, "failed", code, message)));
        }
    }

    private void Expire(string operationId)
    {
        if (!_pending.TryRemove(operationId, out var t)) return;
        t.Dispose();
        events.Publish(new OperationMessage(new OperationResult(operationId, "failed", ErrorCodes.Timeout, "El PC no ha confirmado la acción a tiempo.")));
    }

    private static string Clip(string? s) => s is null ? "" : s.Length > 300 ? s[..300] : s;

    public void Dispose()
    {
        foreach (var t in _pending.Values) t.Dispose();
        _pending.Clear();
    }
}
