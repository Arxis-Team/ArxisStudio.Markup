using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Linq;
using AvaloniaDesigner.Generator.Models;
using Microsoft.CodeAnalysis;

namespace AvaloniaDesigner.Generator.Services
{
    public class PropertyGenerator
    {
        private readonly TypeResolver _resolver;
        private readonly ValueFormatter _formatter;
        private readonly SourceProductionContext _context;
        private readonly string _assemblyName;
        private readonly string _fileName;

        public PropertyGenerator(
            TypeResolver resolver, 
            ValueFormatter formatter, 
            SourceProductionContext context, 
            string assemblyName,
            string fileName)
        {
            _resolver = resolver;
            _formatter = formatter;
            _context = context;
            _assemblyName = assemblyName;
            _fileName = fileName;
        }

        public void GeneratePropertyAssignment(IndentedTextWriter writer,
            string targetName,
            INamedTypeSymbol? targetTypeSymbol,
            string propertyName,
            PropertyModel model)
        {
            if (targetTypeSymbol == null) return;

            // 1. События
            var eventSymbol = _resolver.FindEvent(targetTypeSymbol, propertyName);
            if (eventSymbol != null)
            {
                if (model.Value is string handlerName)
                {
                    writer.WriteLine($"{targetName}.{propertyName} += {handlerName};");
                }
                return;
            }

            IPropertySymbol? targetPropSymbol = _resolver.FindProperty(targetTypeSymbol, propertyName);
            
            if (targetPropSymbol == null)
            {
                if (!propertyName.Contains(".") && propertyName != "Classes" && propertyName != "Name")
                {
                    _context.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.PropertyNotFound, Location.None, propertyName, targetTypeSymbol.Name));
                    return; 
                }
            }

            // 2.1 Привязки
            if (!string.IsNullOrEmpty(model.BindingPath))
            {
                var avaloniaProp = _resolver.FindAvaloniaPropertyField(targetTypeSymbol, propertyName);
                if (avaloniaProp != null)
                {
                    string propField = $"{targetTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}.{avaloniaProp.Name}";
                    string bindingCode = $"new global::Avalonia.Data.Binding(\"{model.BindingPath}\")";
                    
                    var initializers = new List<string>();
                    if (!string.IsNullOrEmpty(model.BindingMode)) initializers.Add($"Mode = global::Avalonia.Data.BindingMode.{model.BindingMode}");
                    if (!string.IsNullOrEmpty(model.BindingStringFormat)) initializers.Add($"StringFormat = \"{model.BindingStringFormat}\"");
                    if (!string.IsNullOrEmpty(model.BindingElementName)) initializers.Add($"ElementName = \"{model.BindingElementName}\"");
                    if (!string.IsNullOrEmpty(model.BindingConverter)) initializers.Add($"Converter = (global::Avalonia.Data.Converters.IValueConverter)this.FindResource(\"{model.BindingConverter}\")");
                    
                    if (model.BindingConverterParameter != null)
                        initializers.Add($"ConverterParameter = {_formatter.Format(model.BindingConverterParameter, null)}");
                    if (model.BindingFallbackValue != null)
                        initializers.Add($"FallbackValue = {_formatter.Format(model.BindingFallbackValue, targetPropSymbol?.Type)}");
                    if (model.BindingTargetNullValue != null)
                        initializers.Add($"TargetNullValue = {_formatter.Format(model.BindingTargetNullValue, targetPropSymbol?.Type)}");

                    if (model.BindingRelativeSource != null)
                    {
                        var rs = model.BindingRelativeSource;
                        string rsCode = $"new global::Avalonia.Data.RelativeSource(global::Avalonia.Data.RelativeSourceMode.{rs.Mode})";
                        var rsProps = new List<string>();
                        if (!string.IsNullOrEmpty(rs.AncestorType))
                        {
                            var ancType = _resolver.ResolveType(rs.AncestorType!);
                            string typeString = ancType != null 
                                ? ancType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                                : $"global::{rs.AncestorType}"; 
                            rsProps.Add($"AncestorType = typeof({typeString})");
                        }
                        if (rs.AncestorLevel.HasValue) rsProps.Add($"AncestorLevel = {rs.AncestorLevel}");
                        if (!string.IsNullOrEmpty(rs.Tree)) rsProps.Add($"Tree = global::Avalonia.VisualTree.TreeType.{rs.Tree}");
                        if (rsProps.Count > 0) rsCode += " { " + string.Join(", ", rsProps) + " }";
                        initializers.Add($"RelativeSource = {rsCode}");
                    }

                    if (initializers.Count > 0) bindingCode += " { " + string.Join(", ", initializers) + " }";

                    writer.WriteLine($"{targetName}.Bind({propField}, {bindingCode});");
                }
                return;
            }

            // 2.2 Ресурсы
            if (!string.IsNullOrEmpty(model.ResourceKey))
            {
                var avaloniaProp = _resolver.FindAvaloniaPropertyField(targetTypeSymbol, propertyName);
                if (avaloniaProp != null)
                {
                    string propField = $"{targetTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}.{avaloniaProp.Name}";
                    writer.WriteLine($"{targetName}.Bind({propField}, this.GetResourceObservable(\"{model.ResourceKey}\"));");
                }
                else if (targetPropSymbol != null)
                {
                    string typeName = targetPropSymbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    writer.WriteLine($"{targetName}.{propertyName} = ({typeName})this.FindResource(\"{model.ResourceKey}\");");
                }
                return;
            }

            // 2.3 Ассеты
            if (!string.IsNullOrEmpty(model.AssetPath))
            {
                if (targetPropSymbol != null)
                {
                    string targetAssembly = !string.IsNullOrEmpty(model.AssetAssembly) ? model.AssetAssembly! : _assemblyName;
                    string cleanPath = model.AssetPath!.TrimStart('/');
                    string uriString = $"avares://{targetAssembly}/{cleanPath}";
                    string streamCode = $"global::Avalonia.Platform.AssetLoader.Open(new global::System.Uri(\"{uriString}\"))";

                    if (_resolver.IsAssignableTo(targetPropSymbol.Type, "Avalonia.Media.IImage") || 
                        _resolver.IsAssignableTo(targetPropSymbol.Type, "Avalonia.Media.Imaging.Bitmap"))
                    {
                        writer.WriteLine($"{targetName}.{propertyName} = new global::Avalonia.Media.Imaging.Bitmap({streamCode});");
                    }
                    else if (_resolver.IsAssignableTo(targetPropSymbol.Type, "Avalonia.Controls.WindowIcon"))
                    {
                        writer.WriteLine($"{targetName}.{propertyName} = new global::Avalonia.Controls.WindowIcon({streamCode});");
                    }
                    else
                    {
                         writer.WriteLine($"{targetName}.{propertyName} = new global::Avalonia.Media.Imaging.Bitmap({streamCode});");
                    }
                }
                return;
            }

            // 2.4 Коллекции
            if (targetPropSymbol != null && _resolver.IsCollectionType(targetPropSymbol.Type))
            {
                if (model.Items is { Count: > 0 })
                {
                    string collectionName = $"{targetName}.{propertyName}";
                    int index = 0;
                    foreach (var elementModel in model.Items)
                    {
                        if (string.IsNullOrEmpty(elementModel.Type)) { index++; continue; }
                        string? varName = GenerateNestedControl(writer, elementModel, $"{propertyName}_{index}");
                        if (varName != null) writer.WriteLine($"{collectionName}.Add({varName});");
                        index++;
                    }
                }
                return;
            }

            // 2.5 Вложенные контролы
            if (!string.IsNullOrEmpty(model.Type))
            {
                string? assignedVarName = GenerateNestedControl(writer, model, propertyName);
                if (assignedVarName != null)
                    writer.WriteLine($"{targetName}.{propertyName} = {assignedVarName};");
                return;
            }

            // 2.6 Обычные значения
            if (model.Value != null)
            {
                if (propertyName.Contains("."))
                {
                    HandleAttachedProperty(writer, targetName, propertyName, model.Value);
                }
                else
                {
                    bool handled = false;
                    if (targetPropSymbol != null && targetPropSymbol.SetMethod == null)
                    {
                        var parseMethod = targetPropSymbol.Type.GetMembers("Parse")
                            .OfType<IMethodSymbol>()
                            .FirstOrDefault(m => m.IsStatic && m.Parameters.Length == 1 && m.Parameters[0].Type.SpecialType == SpecialType.System_String);

                        if (parseMethod != null && _resolver.IsCollectionType(targetPropSymbol.Type))
                        {
                            string typeName = targetPropSymbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                            writer.WriteLine($"{targetName}.{propertyName}.AddRange({typeName}.Parse(\"{model.Value}\"));");
                            handled = true;
                        }
                    }

                    if (!handled)
                    {
                        string valueExpr = _formatter.Format(model.Value, targetPropSymbol?.Type);
                        writer.WriteLine($"{targetName}.{propertyName} = {valueExpr};");
                    }
                }
            }
        }

        private string? GenerateNestedControl(IndentedTextWriter writer, PropertyModel model, string propertyName)
        {
            var objectType = _resolver.ResolveType(model.Type);
            if (objectType == null)
            {
                _context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.TypeNotFound, Location.None, model.Type));
                return null;
            }

            string fullTypeName = $"global::{model.Type}";
            string? controlName = null;

            if (model.Properties.TryGetValue("Name", out var nameProp) && nameProp.Value is string s)
                controlName = s;

            string assignedVarName;
            if (!string.IsNullOrEmpty(controlName))
            {
                assignedVarName = $"this.{controlName}";
                writer.WriteLine($"{assignedVarName} = new {fullTypeName}();");
            }
            else
            {
                assignedVarName = $"_gen_{propertyName}_{Guid.NewGuid().ToString("N").Substring(0, 4)}";
                writer.WriteLine($"{fullTypeName} {assignedVarName} = new {fullTypeName}();");
            }

            if (model.Properties != null)
            {
                foreach (var propEntry in model.Properties)
                {
                    if (propEntry.Key == "Name") continue;
                    GeneratePropertyAssignment(writer, assignedVarName, objectType, propEntry.Key, propEntry.Value);
                }
            }
            return assignedVarName;
        }
        
        private void HandleAttachedProperty(IndentedTextWriter writer, string targetName, string key, object value)
        {
            int lastDot = key.LastIndexOf('.');
            string ownerName = key.Substring(0, lastDot);
            string propName = key.Substring(lastDot + 1);
            
            var ownerType = _resolver.ResolveType(ownerName);
            if (ownerType == null)
            {
                 _context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.TypeNotFound, Location.None, ownerName));
                 return;
            }

            var setter = ownerType != null ? _resolver.FindAttachedSetter(ownerType, propName) : null;

            if (ownerType != null && setter != null)
            {
                var valType = setter.Parameters[1].Type;
                string valueExpr = _formatter.Format(value, valType);

                // Design-Time оптимизация
                bool isDesignTime = ownerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::Avalonia.Controls.Design";
                if (isDesignTime) writer.WriteLine("#if DEBUG");

                writer.WriteLine($"global::{ownerName}.{setter.Name}({targetName}, {valueExpr});");

                if (isDesignTime) writer.WriteLine("#endif");
            }
            else
            {
                 _context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.PropertyNotFound, Location.None, propName, ownerName));
            }
        }
    }
}