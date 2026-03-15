using Microsoft.CodeAnalysis;

namespace ArxisStudio.Markup.Generator.Services
{
    internal class ClassInfo
    {
        internal string ClassName { get; }
        internal string Namespace { get; }
        internal string? BaseClass { get; }

        internal ClassInfo(string className, string ns, string? baseClass)
        {
            ClassName = className;
            Namespace = ns;
            BaseClass = baseClass;
        }
    }

    internal static class RoslynParser
    {
        internal static ClassInfo ParseClassInfo(INamedTypeSymbol typeSymbol)
        {
            var className = typeSymbol.Name;
            var containingNamespace = typeSymbol.ContainingNamespace;
            var ns = containingNamespace == null || containingNamespace.IsGlobalNamespace
                ? "Global"
                : containingNamespace.ToDisplayString();
            var baseClass = typeSymbol.BaseType?.ToDisplayString();

            return new ClassInfo(className, ns, baseClass);
        }
    }
}
