using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using ArxisStudio.Markup.Json;
using ArxisStudio.Markup.Generator.Builders;
using ArxisStudio.Markup.Generator.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace ArxisStudio.Markup.Generator
{
    /// <summary>
    /// Инкрементальный Roslyn generator, компилирующий документы <c>.arxui</c> в код
    /// метода <c>InitializeComponent()</c>.
    /// </summary>
    [Generator(LanguageNames.CSharp)]
    public class ArxuiGenerator : IIncrementalGenerator
    {
        /// <summary>
        /// Регистрирует pipeline обработки <c>.arxui</c> файлов.
        /// </summary>
        /// <param name="context">Контекст инициализации incremental generator.</param>
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var assetsPipeline = context.AdditionalTextsProvider
                .Where(f => Path.GetFileName(f.Path).EndsWith(".arxui", StringComparison.OrdinalIgnoreCase))
                .Select((text, token) => 
                {
                    var result = ParseJson(text);
                    return (Asset: result.Model, FilePath: text.Path, FileName: Path.GetFileName(text.Path), Error: result.Error);
                });

            var combined = context.CompilationProvider.Combine(assetsPipeline.Collect());

            context.RegisterSourceOutput(combined, (spc, source) => 
                Execute(spc, source.Left, source.Right));
        }

        private static (UiDocument? Model, Exception? Error) ParseJson(AdditionalText text)
        {
            var jsonContent = text.GetText()?.ToString();
            if (string.IsNullOrWhiteSpace(jsonContent)) return (null, null);
            try
            {
                var model = ArxuiSerializer.Deserialize(jsonContent!);
                return (model, null);
            }
            catch (Exception ex) { return (null, ex); }
        }

        private void Execute(
            SourceProductionContext context,
            Compilation compilation,
            ImmutableArray<(UiDocument? Document, string FilePath, string FileName, Exception? Error)> inputs)
        {
            if (inputs.IsDefaultOrEmpty) return;

            var typeResolver = new TypeResolver(compilation);
            var builder = new ComponentSourceBuilder(typeResolver, context);
            string assemblyName = compilation.AssemblyName ?? "AvaloniaApp";
            var duplicateClasses = inputs
                .Where(i => i.Document is not null && !string.IsNullOrWhiteSpace(i.Document.Class))
                .GroupBy(i => i.Document!.Class!, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .ToDictionary(
                    g => g.Key,
                    g => string.Join(", ", g.Select(i => i.FileName).OrderBy(n => n, StringComparer.Ordinal)),
                    StringComparer.Ordinal);

            foreach (var input in inputs)
            {
                if (input.Error != null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.JsonParseError, Location.None, input.FileName, input.Error.Message));
                    continue;
                }
                if (input.Document is null) continue;

                var rootType = typeResolver.ResolveType(input.Document.Root.TypeName);
                if (rootType == null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.TypeNotFound,
                        Location.None,
                        input.Document.Root.TypeName));
                    continue;
                }

                if (!IsDocumentKindCompatible(input.Document.Kind, rootType, typeResolver))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.RootTypeKindMismatch,
                        Location.None,
                        input.FileName,
                        input.Document.Root.TypeName,
                        input.Document.Kind));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(input.Document.Class))
                {
                    if (RequiresTargetClass(input.Document.Kind))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            DiagnosticDescriptors.TargetClassMissing,
                            Location.None,
                            input.FileName));
                    }
                    continue;
                }

                var targetClassName = input.Document.Class!;
                var targetType = compilation.GetTypeByMetadataName(targetClassName);
                if (targetType == null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.TargetClassNotFound,
                        Location.None,
                        input.FileName,
                        targetClassName));
                    continue;
                }

                var classInfo = RoslynParser.ParseClassInfo(targetType);

                if (duplicateClasses.TryGetValue(targetClassName, out var duplicateFiles))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.DuplicateTargetClass,
                        Location.None,
                        targetClassName,
                        duplicateFiles));
                    continue;
                }

                if (!IsPartial(targetType))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.TargetClassMustBePartial,
                        Location.None,
                        input.FileName,
                        targetClassName));
                    continue;
                }

                if (!IsDocumentKindCompatible(input.Document.Kind, targetType, typeResolver))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.DocumentKindMismatch,
                        Location.None,
                        input.FileName,
                        input.Document.Kind,
                        targetClassName));
                    continue;
                }

                if (!typeResolver.IsAssignableTo(targetType, input.Document.Root.TypeName))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.RootTypeTargetClassMismatch,
                        Location.None,
                        input.FileName,
                        input.Document.Root.TypeName,
                        targetClassName));
                    continue;
                }

                if (!ValidateElementNames(context, input.Document, input.FileName))
                {
                    continue;
                }

                try
                {
                    string code = builder.Build(input.Document, classInfo, assemblyName, input.FileName);
                    string hintName = $"{classInfo.Namespace}.{classInfo.ClassName}.g.cs";
                    context.AddSource(hintName, SourceText.From(code, Encoding.UTF8));
                }
                catch (Exception ex)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.GeneratorCrash,
                        Location.None,
                        input.FileName,
                        ex.Message));
                }
            }
        }

        private static bool ValidateElementNames(SourceProductionContext context, UiDocument document, string fileName)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            var duplicateNames = new HashSet<string>(StringComparer.Ordinal);
            var hasErrors = false;

            VisitNode(document.Root);
            return !hasErrors;

            void VisitNode(UiNode node)
            {
                if (node.Properties.TryGetValue("Name", out var nameValue) &&
                    nameValue is ScalarValue { Value: string name } &&
                    !string.IsNullOrWhiteSpace(name))
                {
                    if (!CSharpCode.IsValidIdentifier(name))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            DiagnosticDescriptors.InvalidElementName,
                            Location.None,
                            name,
                            fileName));
                        hasErrors = true;
                    }

                    if (!names.Add(name) && duplicateNames.Add(name))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            DiagnosticDescriptors.DuplicateElementName,
                            Location.None,
                            name,
                            fileName));
                        hasErrors = true;
                    }
                }

                VisitStyles(node.Styles);
                VisitResources(node.Resources);

                foreach (var property in node.Properties)
                {
                    VisitValue(property.Value);
                }
            }

            void VisitValue(UiValue value)
            {
                switch (value)
                {
                    case NodeValue nodeValue:
                        VisitNode(nodeValue.Node);
                        break;
                    case CollectionValue collectionValue:
                        foreach (var item in collectionValue.Items)
                        {
                            VisitValue(item);
                        }
                        break;
                }
            }

            void VisitStyles(UiStyles? styles)
            {
                if (styles == null)
                {
                    return;
                }

                foreach (var item in styles.Items)
                {
                    if (item is StyleNodeValue styleNode)
                    {
                        VisitNode(styleNode.Node);
                    }
                }
            }

            void VisitResources(UiResources? resources)
            {
                if (resources == null)
                {
                    return;
                }

                foreach (var value in resources.Values.Values)
                {
                    VisitValue(value);
                }
            }
        }

        private static bool IsPartial(INamedTypeSymbol typeSymbol)
        {
            foreach (var syntaxReference in typeSymbol.DeclaringSyntaxReferences)
            {
                if (syntaxReference.GetSyntax() is ClassDeclarationSyntax classDeclaration &&
                    classDeclaration.Modifiers.Any(m => m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword)))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool RequiresTargetClass(UiDocumentKind kind)
        {
            return kind is UiDocumentKind.Application or UiDocumentKind.Control or UiDocumentKind.Window;
        }

        private static bool IsDocumentKindCompatible(UiDocumentKind kind, INamedTypeSymbol targetType, TypeResolver typeResolver)
        {
            return kind switch
            {
                UiDocumentKind.Application => typeResolver.IsAssignableTo(targetType, "Avalonia.Application"),
                UiDocumentKind.Window => typeResolver.IsAssignableTo(targetType, "Avalonia.Controls.Window"),
                UiDocumentKind.Control => typeResolver.IsAssignableTo(targetType, "Avalonia.Controls.Control"),
                UiDocumentKind.Styles => typeResolver.IsAssignableTo(targetType, "Avalonia.Styling.Styles"),
                UiDocumentKind.ResourceDictionary => typeResolver.IsAssignableTo(targetType, "Avalonia.Controls.ResourceDictionary"),
                _ => true
            };
        }
    }
}
