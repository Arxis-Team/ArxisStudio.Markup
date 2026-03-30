using System;
using System.Collections.Immutable;
using System.Linq;
using Avalonia.Controls;
using ArxisStudio.Markup.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit.Sdk;

namespace ArxisStudio.Markup.Generator.Tests
{
    internal static class GeneratorTestHelper
    {
        /// <summary>
        /// Запускает генератор с указанным исходником и дополнительными файлами.
        /// </summary>
        /// <param name="userSource">Исходный код пользовательского типа.</param>
        /// <param name="userSourcePath">Путь к исходному файлу пользовательского типа.</param>
        /// <param name="additionalFiles">Дополнительные файлы для генератора.</param>
        /// <returns>Результат выполнения генератора.</returns>
        public static GeneratorDriverRunResult RunGenerator(
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

            return driver.GetRunResult();
        }

        /// <summary>
        /// Возвращает текст сгенерированного файла по имени подсказки.
        /// </summary>
        /// <param name="userSource">Исходный код пользовательского типа.</param>
        /// <param name="userSourcePath">Путь к исходному файлу пользовательского типа.</param>
        /// <param name="hintName">Имя ожидаемого сгенерированного файла.</param>
        /// <param name="additionalFiles">Дополнительные файлы для генератора.</param>
        /// <returns>Текст сгенерированного исходника.</returns>
        public static string GetGeneratedSource(
            string userSource,
            string userSourcePath,
            string hintName,
            params (string path, string content)[] additionalFiles)
        {
            var runResult = RunGenerator(userSource, userSourcePath, additionalFiles);
            var generatedSources = runResult.Results
                .SelectMany(r => r.GeneratedSources)
                .ToImmutableArray();
            var generated = generatedSources.FirstOrDefault(source => source.HintName == hintName);

            if (generated.HintName == null)
            {
                throw new XunitException(
                    $"Generated source '{hintName}' was not found. Available: {string.Join(", ", generatedSources.Select(source => source.HintName))}");
            }

            return generated.SourceText.ToString();
        }

        /// <summary>
        /// Возвращает диагностические сообщения, выданные генератором.
        /// </summary>
        /// <param name="userSource">Исходный код пользовательского типа.</param>
        /// <param name="userSourcePath">Путь к исходному файлу пользовательского типа.</param>
        /// <param name="additionalFiles">Дополнительные файлы для генератора.</param>
        /// <returns>Набор диагностик генератора.</returns>
        public static ImmutableArray<Diagnostic> GetGeneratorDiagnostics(
            string userSource,
            string userSourcePath,
            params (string path, string content)[] additionalFiles)
        {
            var runResult = RunGenerator(userSource, userSourcePath, additionalFiles);

            return runResult.Results
                .SelectMany(r => r.Diagnostics)
                .ToImmutableArray();
        }
    }
}
