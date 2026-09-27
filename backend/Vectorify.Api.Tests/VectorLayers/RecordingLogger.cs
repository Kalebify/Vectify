using Microsoft.Extensions.Logging;

namespace Vectorify.Api.Tests.VectorLayers;

/// <summary>
/// ILogger&lt;T&gt; en memoria que solo graba el mensaje ya formateado de cada
/// entrada -- suficiente para verificar telemetría puntual (ej. "capa
/// {GroupId} pintada con fill {ColorHex}", M2.1-S01) sin depender de ningún
/// framework de logging real.
/// </summary>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    public List<string> Messages { get; } = new();

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        Messages.Add(formatter(state, exception));
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose()
        {
        }
    }
}
