#if NETSTANDARD2_0
// Compiler support types that netstandard2.0 lacks. They let the SDK use records and init-only
// setters on every target framework.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}
#endif
