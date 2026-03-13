using Microsoft.CodeAnalysis;

namespace ArxisStudio.Markup.Json.Generator
{
    public static class DiagnosticDescriptors
    {
        public static readonly DiagnosticDescriptor JsonParseError = new DiagnosticDescriptor("ADG0001", "JSON Parsing Error", "Failed to parse asset file '{0}': {1}", "Design", DiagnosticSeverity.Error, true);
        public static readonly DiagnosticDescriptor TypeNotFound = new DiagnosticDescriptor("ADG0002", "Type Not Found", "Type '{0}' not found", "Design", DiagnosticSeverity.Error, true);
        public static readonly DiagnosticDescriptor PropertyNotFound = new DiagnosticDescriptor("ADG0003", "Property Not Found", "Property '{0}' not found on '{1}'", "Design", DiagnosticSeverity.Warning, true);
        public static readonly DiagnosticDescriptor EventNotFound = new DiagnosticDescriptor("ADG0004", "Event Not Found", "Event '{0}' not found on '{1}'", "Design", DiagnosticSeverity.Error, true);

        public static readonly DiagnosticDescriptor PartialClassNotFound = new DiagnosticDescriptor(
            id: "ADG0005",
            title: "Partial Class Not Found",
            messageFormat: "Could not find a C# partial class file for '{0}'. Expected '{1}' in the project.",
            category: "Design",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor AmbiguousPartialClass = new DiagnosticDescriptor(
            id: "ADG0006",
            title: "Ambiguous Partial Class",
            messageFormat: "Found multiple C# files matching '{0}': {1}. Please rename files to be unique.",
            category: "Design",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);
    }
}
