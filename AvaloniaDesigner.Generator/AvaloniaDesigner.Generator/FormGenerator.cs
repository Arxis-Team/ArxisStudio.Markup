using System;
using System.Collections.Immutable;
using System.IO;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using AvaloniaDesigner.Generator.Models;
using AvaloniaDesigner.Generator.Services;
using AvaloniaDesigner.Generator.Builders;
using Newtonsoft.Json; // НОВОЕ ИСПОЛЬЗОВАНИЕ
using Newtonsoft.Json.Serialization; // Для настройки CamelCase

namespace AvaloniaDesigner.Generator
{
    [Generator(LanguageNames.CSharp)]
    public class FormGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var jsonFiles = context.AdditionalTextsProvider
                .Where(f => Path.GetFileName(f.Path).EndsWith("Model.json", StringComparison.OrdinalIgnoreCase));

            var compilationAndFiles = context.CompilationProvider.Combine(jsonFiles.Collect());

            context.RegisterSourceOutput(compilationAndFiles, (spc, source) => Execute(spc, source.Left, source.Right));
        }

        private void Execute(
            SourceProductionContext context,
            Compilation compilation,
            ImmutableArray<AdditionalText> files)
        {
            // 🛑 ДОБАВЬТЕ ЭТУ УНИКАЛЬНУЮ ТРАССИРОВКУ В ПЕРВУЮ ЖЕ СТРОКУ
            context.ReportDiagnostic(Diagnostic.Create(
                new DiagnosticDescriptor("ADG0005", "Generator Version Check", $"Running new generator code version {DateTime.Now.ToString("HHmmss")}.", "Debug", DiagnosticSeverity.Warning, true), 
                Location.None));
            
            string rootNamespace = compilation.AssemblyName ?? "DefaultApp";
            
            var typeResolver = new TypeResolver(compilation);
            var builder = new FormClassBuilder(typeResolver, context);

            foreach (var file in files)
            {
                var jsonContent = file.GetText(context.CancellationToken)?.ToString();
                if (string.IsNullOrEmpty(jsonContent)) continue;

                try
                {
                    //  ИЗМЕНЕНИЕ: ИСПОЛЬЗУЕМ NEWTONSOFT.JSON ДЛЯ ДЕСЕРИАЛИЗАЦИИ
                    var avaloniaModel = JsonConvert.DeserializeObject<AvaloniaModel>(
                        jsonContent!, 
                        new JsonSerializerSettings { 
                            // Настройка для корректного сопоставления JSON (camelCase) с C# (PascalCase)
                            ContractResolver = new DefaultContractResolver 
                            { 
                                NamingStrategy = new CamelCaseNamingStrategy(false, true) 
                            }
                        });

                    if (avaloniaModel is null) 
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            new DiagnosticDescriptor("ADG0003", "Deserialization Error", 
                            $"Deserialization resulted in null model for {Path.GetFileName(file.Path)}. Content might be empty or invalid.", 
                            "Generation", DiagnosticSeverity.Error, true), 
                            Location.None));
                        continue;
                    }

                    string code = builder.Build(avaloniaModel, rootNamespace);

                    context.AddSource($"{avaloniaModel.FormName}.g.cs", SourceText.From(code, Encoding.UTF8));
                }
                catch (Newtonsoft.Json.JsonException ex) //  Обрабатываем исключение Newtonsoft.Json
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        new DiagnosticDescriptor("ADG0001", "JSON Parsing Error", 
                        $"Error parsing {Path.GetFileName(file.Path)}: {ex.Message}", 
                        "Generation", DiagnosticSeverity.Error, true), 
                        Location.None));
                }
                catch (Exception ex)
                {
                     context.ReportDiagnostic(Diagnostic.Create(
                        new DiagnosticDescriptor("ADG0004", "General Error", 
                        $"Unhandled exception during processing {Path.GetFileName(file.Path)}: {ex.Message}", 
                        "Generation", DiagnosticSeverity.Error, true), 
                        Location.None));
                }
            }
        }
    }
}