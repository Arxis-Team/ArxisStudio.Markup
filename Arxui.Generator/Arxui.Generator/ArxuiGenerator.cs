using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using ArxisStudio.Markup.Json;
using ArxisStudio.Markup.Json.Generator.Builders;
using ArxisStudio.Markup.Json.Generator.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace ArxisStudio.Markup.Json.Generator
{
    [Generator(LanguageNames.CSharp)]
    public class ArxuiGenerator : IIncrementalGenerator
    {
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

            foreach (var input in inputs)
            {
                if (input.Error != null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.JsonParseError, Location.None, input.FileName, input.Error.Message));
                    continue;
                }
                if (input.Document is null) continue;

                string baseName = Path.GetFileNameWithoutExtension(input.FileName);
                string targetCs = $"{baseName}.arxui.cs";

                var matchingTrees = compilation.SyntaxTrees.Where(t => 
                {
                    string fName = Path.GetFileName(t.FilePath);
                    return fName.Equals(targetCs, StringComparison.OrdinalIgnoreCase);
                }).ToList();

                if (matchingTrees.Count == 0)
                {
                    context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.PartialClassNotFound, Location.None, input.FileName, targetCs));
                    continue;
                }

                if (matchingTrees.Count > 1)
                {
                    string foundPaths = string.Join(", ", matchingTrees.Select(t => t.FilePath));
                    context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.AmbiguousPartialClass, Location.None, input.FileName, foundPaths));
                    continue;
                }

                var classInfo = RoslynParser.ParseClassInfo(matchingTrees[0]);
                if (classInfo == null) continue;

                try
                {
                    string code = builder.Build(input.Document, classInfo, assemblyName, input.FileName);
                    string hintName = $"{classInfo.Namespace}.{classInfo.ClassName}.g.cs";
                    context.AddSource(hintName, SourceText.From(code, Encoding.UTF8));
                }
                catch (Exception ex)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        new DiagnosticDescriptor("ADG9999", "Crash", $"{ex.Message}", "Gen", DiagnosticSeverity.Error, true), Location.None));
                }
            }
        }
    }
}
