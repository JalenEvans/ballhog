// Assembly marker for the Ballhog library.
// Client/provider/cache implementation arrives in later chunks.
// The test assembly is granted access to internal transport types below.

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Ballhog.Tests")]

namespace Ballhog;

internal static class AssemblyMarker;
