using System.Runtime.CompilerServices;

// The unchecked genome factory exists so validator tests can plant VAL-005
// violations. The public constructors still reject those values.
[assembly: InternalsVisibleTo("Martis.Domain.Tests")]
