// Assembly marker for the Ballhog library.
// S1 is complete: the typed client, HTTP-backed provider, cache-aside decorator, source pacer,
// and DI wiring all ship. Comments-only marker; no runtime behavior lives here.
// The test assembly is granted access to internal transport types below.

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Ballhog.Tests")]

namespace Ballhog;

internal static class AssemblyMarker;
