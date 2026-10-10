using System.Net.Http;

namespace Ballhog.Tests.Infrastructure;

/// <summary>
/// A scripted <see cref="HttpMessageHandler"/> used to drive transport tests fully
/// offline. It records every <see cref="HttpRequestMessage"/> it receives (letting a
/// test inspect the headers a delegating handler applied) and answers each call from
/// either a pre-scripted sequence of responses or a responder callback.
/// </summary>
/// <remarks>
/// The stub returns completed tasks synchronously (and converts a responder throw into
/// a faulted task) so retry/backoff timing stays deterministic under
/// <c>FakeTimeProvider</c>: a delegating handler's continuation runs inline instead of
/// hopping to the thread pool.
/// </remarks>
internal sealed class ScriptedHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, int, HttpResponseMessage> _responder;

    /// <summary>Initializes the stub with a fixed sequence of responses, consumed in order.</summary>
    public ScriptedHttpMessageHandler(params HttpResponseMessage[] responses)
        : this((IEnumerable<HttpResponseMessage>)responses)
    {
    }

    /// <summary>Initializes the stub with a fixed sequence of responses, consumed in order.</summary>
    public ScriptedHttpMessageHandler(IEnumerable<HttpResponseMessage> responses)
        : this(BuildSequentialResponder(responses))
    {
    }

    /// <summary>
    /// Initializes the stub with a responder invoked for every request; the second
    /// argument is the 1-based call number, enabling scripted per-attempt behavior.
    /// </summary>
    public ScriptedHttpMessageHandler(Func<HttpRequestMessage, int, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    /// <summary>Every request the stub has handled, in call order.</summary>
    public List<HttpRequestMessage> Requests { get; } = new();

    /// <summary>The number of requests the stub has handled.</summary>
    public int CallCount => Requests.Count;

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        try
        {
            return Task.FromResult(_responder(request, Requests.Count));
        }
        catch (Exception exception)
        {
            return Task.FromException<HttpResponseMessage>(exception);
        }
    }

    private static Func<HttpRequestMessage, int, HttpResponseMessage> BuildSequentialResponder(IEnumerable<HttpResponseMessage> responses)
    {
        var queue = new Queue<HttpResponseMessage>(responses);
        return (_, _) => queue.Count > 0
            ? queue.Dequeue()
            : throw new InvalidOperationException("The scripted response sequence is exhausted.");
    }
}
