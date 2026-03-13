using System;
using System.Collections.Immutable;
using System.Linq;
using Avalonia.Controls;
using ArxisStudio.Markup.Json.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit.Sdk;

namespace ArxisStudio.Markup.Json.Generator.Tests
{
    internal static class GeneratorTestHelper
    {
        public static ImmutableArray<GeneratedSourceResult> RunGenerator(
            string userSource,
            string userSourcePath,
            params (string path, string content)[] additionalFiles)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(userSource, path: userSourcePath);

            // 🔹 Берём ВСЕ загруженные сборки (включая Avalonia) и добавляем как MetadataReference
            var references = AppDomain.CurrentDomain
                .GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Select(a => MetadataReference.CreateFromFile(a.Location))
                .Cast<MetadataReference>()
                .ToList();

            var avaloniaReference = MetadataReference.CreateFromFile(typeof(Control).Assembly.Location);
            if (!references.Any(reference => reference.Display == avaloniaReference.Display))
            {
                references.Add(avaloniaReference);
            }

            var compilation = CSharpCompilation.Create(
                assemblyName: "TestAssembly",
                syntaxTrees: new[] { syntaxTree },
                references: references,
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var additionalTexts = additionalFiles
                .Select(f => (AdditionalText)new InMemoryAdditionalText(f.path, f.content))
                .ToImmutableArray();

            var generator = new ArxuiGenerator();
            var sourceGenerator = generator.AsSourceGenerator();

            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                [sourceGenerator],
                additionalTexts: additionalTexts);

            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out var outputCompilation,
                out var diagnostics);

            var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
            if (errors.Length > 0)
            {
                throw new Xunit.Sdk.XunitException(
                    "Generator produced compilation errors:\n" +
                    string.Join("\n", errors.Select(e => e.ToString())));
            }

            var runResult = driver.GetRunResult();

            return runResult.Results
                .SelectMany(r => r.GeneratedSources)
                .ToImmutableArray();
        }

        public static string GetGeneratedSource(
            string userSource,
            string userSourcePath,
            string hintName,
            params (string path, string content)[] additionalFiles)
        {
            var generatedSources = RunGenerator(userSource, userSourcePath, additionalFiles);
            var generated = generatedSources.FirstOrDefault(source => source.HintName == hintName);

            if (generated.HintName == null)
            {
                throw new XunitException(
                    $"Generated source '{hintName}' was not found. Available: {string.Join(", ", generatedSources.Select(source => source.HintName))}");
            }

            return generated.SourceText.ToString();
        }
    }
}
