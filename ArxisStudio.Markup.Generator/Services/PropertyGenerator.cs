using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Linq;
using ArxisStudio.Markup;
using ArxisStudio.Markup.Json;
using Microsoft.CodeAnalysis;

namespace ArxisStudio.Markup.Generator.Services
{
    internal class PropertyGenerator
    {
        private readonly TypeResolver _resolver;
        private readonly ValueFormatter _formatter;
        private readonly SourceProductionContext _context;
        private readonly string _assemblyName;
        private readonly Dictionary<string, int> _generatedLocalCounters = new(StringComparer.Ordinal);

        internal PropertyGenerator(
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
        }

        internal void GeneratePropertyAssignment(IndentedTextWriter writer,
            string targetName,
            INamedTypeSymbol? targetTypeSymbol,
            string propertyName,
            UiValue value)
        {
            if (targetTypeSymbol == null) return;

            // 1. События
            var eventSymbol = _resolver.FindEvent(targetTypeSymbol, propertyName);
            if (eventSymbol != null)
            {
                if (value is ScalarValue { Value: string handlerName })
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
            if (value is BindingValue bindingValue)
            {
                var avaloniaProp = _resolver.FindAvaloniaPropertyField(targetTypeSymbol, propertyName);
                if (avaloniaProp != null)
                {
                    string propField = $"{targetTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}.{avaloniaProp.Name}";
                    string bindingCode = $"new global::Avalonia.Data.Binding({CSharpCode.StringLiteral(bindingValue.Binding.Path)})";
                    
                    var initializers = new List<string>();
                    if (bindingValue.Binding.Mode.HasValue) initializers.Add($"Mode = global::Avalonia.Data.BindingMode.{bindingValue.Binding.Mode.Value}");
                    if (!string.IsNullOrEmpty(bindingValue.Binding.StringFormat)) initializers.Add($"StringFormat = {CSharpCode.StringLiteral(bindingValue.Binding.StringFormat!)}");
                    if (!string.IsNullOrEmpty(bindingValue.Binding.ElementName)) initializers.Add($"ElementName = {CSharpCode.StringLiteral(bindingValue.Binding.ElementName!)}");
                    if (!string.IsNullOrEmpty(bindingValue.Binding.ConverterKey)) initializers.Add($"Converter = (global::Avalonia.Data.Converters.IValueConverter)this.FindResource({CSharpCode.StringLiteral(bindingValue.Binding.ConverterKey!)})");
                    
                    if (bindingValue.Binding.ConverterParameter != null)
                        initializers.Add($"ConverterParameter = {_formatter.Format(bindingValue.Binding.ConverterParameter, null)}");
                    if (bindingValue.Binding.FallbackValue != null)
                        initializers.Add($"FallbackValue = {_formatter.Format(bindingValue.Binding.FallbackValue, targetPropSymbol?.Type)}");
                    if (bindingValue.Binding.TargetNullValue != null)
                        initializers.Add($"TargetNullValue = {_formatter.Format(bindingValue.Binding.TargetNullValue, targetPropSymbol?.Type)}");

                    if (bindingValue.Binding.RelativeSource != null)
                    {
                        var rs = bindingValue.Binding.RelativeSource;
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
            if (value is ResourceValue resourceValue)
            {
                var avaloniaProp = _resolver.FindAvaloniaPropertyField(targetTypeSymbol, propertyName);
                if (avaloniaProp != null)
                {
                    string propField = $"{targetTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}.{avaloniaProp.Name}";
                    writer.WriteLine($"{targetName}.Bind({propField}, this.GetResourceObservable({CSharpCode.StringLiteral(resourceValue.Key)}));");
                }
                else if (targetPropSymbol != null)
                {
                    string typeName = targetPropSymbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    writer.WriteLine($"{targetName}.{propertyName} = ({typeName})this.FindResource({CSharpCode.StringLiteral(resourceValue.Key)});");
                }
                return;
            }

            // 2.3 Ассеты
            if (value is UriReferenceValue assetReference)
            {
                if (targetPropSymbol != null)
                {
                    string targetAssembly = !string.IsNullOrEmpty(assetReference.Assembly) ? assetReference.Assembly! : _assemblyName;
                    string cleanPath = assetReference.Path.TrimStart('/');
                    string uriString = $"avares://{targetAssembly}/{cleanPath}";
                    string streamCode = $"global::Avalonia.Platform.AssetLoader.Open(new global::System.Uri({CSharpCode.StringLiteral(uriString)}))";

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
            if (targetPropSymbol != null && _resolver.IsCollectionType(targetPropSymbol.Type) && value is CollectionValue collectionValue)
            {
                if (collectionValue.Items.Count > 0)
                {
                    string collectionName = $"{targetName}.{propertyName}";
                    int index = 0;
                    foreach (var elementModel in collectionValue.Items)
                    {
                        if (elementModel is NodeValue nodeItem)
                        {
                            string? varName = GenerateNestedObject(writer, nodeItem.Node, $"{propertyName}_{index}");
                            if (varName != null) writer.WriteLine($"{collectionName}.Add({varName});");
                        }
                        else if (elementModel is ScalarValue scalarItem && scalarItem.Value != null)
                        {
                            string valueExpr = _formatter.Format(scalarItem.Value, null);
                            writer.WriteLine($"{collectionName}.Add({valueExpr});");
                        }
                        index++;
                    }
                }
                return;
            }

            // 2.5 Вложенные контролы
            if (value is NodeValue nodeValue)
            {
                string? assignedVarName = GenerateNestedObject(writer, nodeValue.Node, propertyName);
                if (assignedVarName != null)
                    writer.WriteLine($"{targetName}.{propertyName} = {assignedVarName};");
                return;
            }

            // 2.6 Обычные значения
            if (value is ScalarValue scalarValue && scalarValue.Value != null)
            {
                if (propertyName.Contains("."))
                {
                    HandleAttachedProperty(writer, targetName, propertyName, scalarValue.Value);
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
                            writer.WriteLine($"{targetName}.{propertyName}.AddRange({typeName}.Parse({_formatter.Format(scalarValue.Value, null)}));");
                            handled = true;
                        }
                    }

                    if (!handled)
                    {
                        string valueExpr = _formatter.Format(scalarValue.Value, targetPropSymbol?.Type);
                        writer.WriteLine($"{targetName}.{propertyName} = {valueExpr};");
                    }
                }
            }
        }

        internal void GenerateHostResources(IndentedTextWriter writer, string targetName, UiResources? resources)
        {
            if (resources == null)
            {
                return;
            }

            foreach (var include in resources.MergedDictionaries)
            {
                writer.WriteLine(
                    $"{targetName}.Resources.MergedDictionaries.Add(new global::Avalonia.Markup.Xaml.Styling.ResourceInclude(new global::System.Uri({CSharpCode.StringLiteral(include.Source)})) {{ Source = new global::System.Uri({CSharpCode.StringLiteral(include.Source)}) }});");
            }

            foreach (var entry in resources.Values)
            {
                var assignment = GenerateResourceValueExpression(writer, entry.Value, $"resource_{CSharpCode.SanitizeIdentifierPart(entry.Key)}");
                if (assignment != null)
                {
                    writer.WriteLine($"{targetName}.Resources[{CSharpCode.StringLiteral(entry.Key)}] = {assignment};");
                }
            }
        }

        internal void GenerateHostStyles(IndentedTextWriter writer, string targetName, UiStyles? styles)
        {
            if (styles == null)
            {
                return;
            }

            var index = 0;
            foreach (var style in styles.Items)
            {
                switch (style)
                {
                    case StyleIncludeValue include:
                    {
                        writer.WriteLine(
                            $"{targetName}.Styles.Add(new global::Avalonia.Markup.Xaml.Styling.StyleInclude(new global::System.Uri({CSharpCode.StringLiteral(include.Source)})) {{ Source = new global::System.Uri({CSharpCode.StringLiteral(include.Source)}) }});");
                        break;
                    }
                    case StyleNodeValue node:
                    {
                        var varName = GenerateNestedObject(writer, node.Node, $"style_{index}");
                        if (varName != null)
                        {
                            writer.WriteLine($"{targetName}.Styles.Add({varName});");
                        }

                        break;
                    }
                }

                index++;
            }
        }

        private string? GenerateResourceValueExpression(IndentedTextWriter writer, UiValue value, string identifierHint)
        {
            return value switch
            {
                ScalarValue scalar => scalar.Value != null ? _formatter.Format(scalar.Value, null) : "null",
                NodeValue node => GenerateNestedObject(writer, node.Node, identifierHint),
                ResourceValue resource => $"this.FindResource({CSharpCode.StringLiteral(resource.Key)})",
                UriReferenceValue asset => GenerateAssetExpression(asset),
                _ => null
            };
        }

        private string GenerateAssetExpression(UriReferenceValue assetReference)
        {
            string targetAssembly = !string.IsNullOrEmpty(assetReference.Assembly) ? assetReference.Assembly! : _assemblyName;
            string cleanPath = assetReference.Path.TrimStart('/');
            string uriString = $"avares://{targetAssembly}/{cleanPath}";
            string streamCode = $"global::Avalonia.Platform.AssetLoader.Open(new global::System.Uri({CSharpCode.StringLiteral(uriString)}))";

            return $"new global::Avalonia.Media.Imaging.Bitmap({streamCode})";
        }

        private string? GenerateNestedObject(IndentedTextWriter writer, UiNode node, string propertyName)
        {
            var objectType = _resolver.ResolveType(node.TypeName);
            if (objectType == null)
            {
                _context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.TypeNotFound, Location.None, node.TypeName));
                return null;
            }

            string fullTypeName = $"global::{node.TypeName}";
            string? controlName = null;

            if (node.Properties.TryGetValue("Name", out var nameProp) && nameProp is ScalarValue { Value: string s })
                controlName = s;

            string assignedVarName;
            if (!string.IsNullOrEmpty(controlName))
            {
                assignedVarName = $"this.{CSharpCode.Identifier(controlName)}";
                writer.WriteLine($"{assignedVarName} = new {fullTypeName}();");
            }
            else
            {
                assignedVarName = NextLocalName(node.TypeName, propertyName);
                writer.WriteLine($"var {assignedVarName} = new {fullTypeName}();");
            }

            GenerateHostResources(writer, assignedVarName, node.Resources);
            GenerateHostStyles(writer, assignedVarName, node.Styles);

            foreach (var propEntry in node.Properties)
            {
                if (propEntry.Key == "Name") continue;
                GeneratePropertyAssignment(writer, assignedVarName, objectType, propEntry.Key, propEntry.Value);
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

                writer.WriteLine($"global::{ownerName}.{setter.Name}({targetName}, {valueExpr});");
            }
            else
            {
                 _context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.PropertyNotFound, Location.None, propName, ownerName));
            }
        }

        private string NextLocalName(string typeName, string propertyName)
        {
            var shortTypeName = typeName.Split('.').LastOrDefault();
            var baseName = !string.IsNullOrWhiteSpace(shortTypeName)
                ? CSharpCode.LowerCamelIdentifierPart(shortTypeName!)
                : CSharpCode.LowerCamelIdentifierPart(propertyName);

            if (!_generatedLocalCounters.TryGetValue(baseName, out var count))
            {
                count = 0;
            }

            count++;
            _generatedLocalCounters[baseName] = count;
            return $"{baseName}{count}";
        }
    }
}
