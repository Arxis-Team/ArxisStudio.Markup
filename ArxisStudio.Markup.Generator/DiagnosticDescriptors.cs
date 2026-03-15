using Microsoft.CodeAnalysis;

namespace ArxisStudio.Markup.Generator
{
    /// <summary>
    /// Содержит описатели диагностик, публикуемых source generator.
    /// </summary>
    public static class DiagnosticDescriptors
    {
        /// <summary>
        /// Ошибка разбора JSON-документа.
        /// </summary>
        public static readonly DiagnosticDescriptor JsonParseError = new DiagnosticDescriptor("ADG0001", "JSON Parsing Error", "Failed to parse asset file '{0}': {1}", "Design", DiagnosticSeverity.Error, true);

        /// <summary>
        /// Ошибка разрешения типа.
        /// </summary>
        public static readonly DiagnosticDescriptor TypeNotFound = new DiagnosticDescriptor("ADG0002", "Type Not Found", "Type '{0}' not found", "Design", DiagnosticSeverity.Error, true);

        /// <summary>
        /// Предупреждение о неизвестном свойстве.
        /// </summary>
        public static readonly DiagnosticDescriptor PropertyNotFound = new DiagnosticDescriptor("ADG0003", "Property Not Found", "Property '{0}' not found on '{1}'", "Design", DiagnosticSeverity.Warning, true);

        /// <summary>
        /// Ошибка разрешения события.
        /// </summary>
        public static readonly DiagnosticDescriptor EventNotFound = new DiagnosticDescriptor("ADG0004", "Event Not Found", "Event '{0}' not found on '{1}'", "Design", DiagnosticSeverity.Error, true);

        /// <summary>
        /// Ошибка отсутствующего значения <c>Class</c>.
        /// </summary>
        public static readonly DiagnosticDescriptor TargetClassMissing = new DiagnosticDescriptor("ADG0005", "Target Class Missing", "Asset file '{0}' must declare a 'Class' value with the target CLR type", "Design", DiagnosticSeverity.Error, true);

        /// <summary>
        /// Ошибка разрешения целевого CLR-типа.
        /// </summary>
        public static readonly DiagnosticDescriptor TargetClassNotFound = new DiagnosticDescriptor("ADG0006", "Target Class Not Found", "Could not resolve target CLR type '{1}' declared in '{0}'", "Design", DiagnosticSeverity.Error, true);

        /// <summary>
        /// Ошибка отсутствующего модификатора <c>partial</c> у целевого типа.
        /// </summary>
        public static readonly DiagnosticDescriptor TargetClassMustBePartial = new DiagnosticDescriptor("ADG0007", "Target Class Must Be Partial", "Target CLR type '{1}' declared in '{0}' must be partial", "Design", DiagnosticSeverity.Error, true);

        /// <summary>
        /// Ошибка несовместимости вида документа и целевого CLR-типа.
        /// </summary>
        public static readonly DiagnosticDescriptor DocumentKindMismatch = new DiagnosticDescriptor("ADG0008", "Document Kind Mismatch", "Asset file '{0}' declares kind '{1}' but target CLR type '{2}' is not compatible", "Design", DiagnosticSeverity.Error, true);

        /// <summary>
        /// Ошибка дублирования нескольких документов для одного CLR-типа.
        /// </summary>
        public static readonly DiagnosticDescriptor DuplicateTargetClass = new DiagnosticDescriptor("ADG0009", "Duplicate Target Class", "Target CLR type '{0}' is declared by multiple asset files: {1}", "Design", DiagnosticSeverity.Error, true);

        /// <summary>
        /// Ошибка несовместимости вида документа и root type.
        /// </summary>
        public static readonly DiagnosticDescriptor RootTypeKindMismatch = new DiagnosticDescriptor("ADG0010", "Root Type Kind Mismatch", "Asset file '{0}' declares root type '{1}' which is not compatible with document kind '{2}'", "Design", DiagnosticSeverity.Error, true);

        /// <summary>
        /// Ошибка несовместимости root type и target class.
        /// </summary>
        public static readonly DiagnosticDescriptor RootTypeTargetClassMismatch = new DiagnosticDescriptor("ADG0011", "Root Type Target Class Mismatch", "Asset file '{0}' declares root type '{1}' which is not compatible with target CLR type '{2}'", "Design", DiagnosticSeverity.Error, true);

        /// <summary>
        /// Неперехваченное исключение во время генерации.
        /// </summary>
        public static readonly DiagnosticDescriptor GeneratorCrash = new DiagnosticDescriptor("ADG9999", "Generator Crash", "Unhandled exception while generating '{0}': {1}", "Gen", DiagnosticSeverity.Error, true);
    }
}
