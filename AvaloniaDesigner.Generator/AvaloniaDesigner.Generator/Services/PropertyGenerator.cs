using System.Text;
using AvaloniaDesigner.Generator.Models;
using Microsoft.CodeAnalysis;
using System.Text.Json;
using System;
using System.Linq;

namespace AvaloniaDesigner.Generator.Services
{
    public class PropertyGenerator
    {
        private readonly TypeResolver _resolver;
        private readonly ValueFormatter _formatter;
        private readonly SourceProductionContext _context;

        public PropertyGenerator(TypeResolver resolver, ValueFormatter formatter, SourceProductionContext context)
        {
            _resolver = resolver;
            _formatter = formatter;
            _context = context;
        }
        
        public string? GeneratePropertyAssignment(
            StringBuilder sb, 
            string targetName, 
            INamedTypeSymbol? targetTypeSymbol, 
            string propertyName, 
            PropertyModel model)
        {
            IPropertySymbol? targetPropSymbol = targetTypeSymbol != null 
                ? _resolver.FindProperty(targetTypeSymbol, propertyName) 
                : null;
            ITypeSymbol? valueTypeSymbol = targetPropSymbol?.Type;

            // --- 1. ОБРАБОТКА СВОЙСТВ-КОЛЛЕКЦИЙ (Children, Items) ---
            if (targetPropSymbol != null && _resolver.IsCollectionType(targetPropSymbol.Type))
            {
                string collectionName = $"{targetName}.{propertyName}";
                _context.ReportDiagnostic(Diagnostic.Create(
                    new DiagnosticDescriptor("ADG9910", "Code Gen: Collection Block", $"Target {targetName}.{propertyName} recognized as Collection.", "Debug", DiagnosticSeverity.Warning, true), 
                    Location.None));
                
                if (model.Properties == null) return null; 

                foreach (var elementEntry in model.Properties.OrderBy(e => e.Key))
                {
                    var elementModel = elementEntry.Value;
                    
                    if (string.IsNullOrEmpty(elementModel.Type)) continue;
                    
                    // Рекурсивно генерируем сам элемент (this.MyCheckBox)
                    string? elementVarName = GenerateNestedControl(
                        sb, 
                        elementModel, 
                        elementEntry.Key, 
                        targetTypeSymbol: null); 
                    
                    // Добавляем созданный элемент в коллекцию 
                    if (!string.IsNullOrEmpty(elementVarName))
                    {
                        sb.AppendLine($"            {collectionName}.Add({elementVarName});");
                        _context.ReportDiagnostic(Diagnostic.Create(
                            new DiagnosticDescriptor("ADG9911", "Code Gen: Collection Add", $"Added {elementVarName} to {collectionName}.", "Debug", DiagnosticSeverity.Warning, true), 
                            Location.None));
                    }
                }
                return null;
            }
            
            // --- 2. ОБРАБОТКА ВЛОЖЕННОГО ОБЪЕКТА/КОНТРОЛА (Content, Child, Template) ---
            if (!string.IsNullOrEmpty(model.Type))
            {
                _context.ReportDiagnostic(Diagnostic.Create(
                    new DiagnosticDescriptor("ADG9912", "Code Gen: Nested Control Block", $"Generating nested control {propertyName} of type {model.Type} for {targetName}.", "Debug", DiagnosticSeverity.Warning, true), 
                    Location.None));

                string? assignedVarName = GenerateNestedControl(sb, model, propertyName, targetTypeSymbol);
                
                if (!string.IsNullOrEmpty(assignedVarName))
                {
                    sb.AppendLine($"            {targetName}.{propertyName} = {assignedVarName};");
                }
                return assignedVarName; 
            }
            
            // --- 3. ОБРАБОТКА ПРИМИТИВНОГО ЗНАЧЕНИЯ (Width, Height, Name, Attached) ---
            if (model.Value.HasValue)
            {
                string propertyKey = propertyName;
                
                if (propertyKey.Contains("."))
                {
                    _context.ReportDiagnostic(Diagnostic.Create(
                        new DiagnosticDescriptor("ADG9913", "Code Gen: Attached Property", $"Handling attached property {propertyKey} for {targetName}.", "Debug", DiagnosticSeverity.Warning, true), 
                        Location.None));

                    HandleAttachedProperty(sb, targetName, propertyKey, model.Value.Value);
                }
                else
                {
                    _context.ReportDiagnostic(Diagnostic.Create(
                        new DiagnosticDescriptor("ADG9914", "Code Gen: Primitive Property", $"Setting primitive property {propertyKey} on {targetName}.", "Debug", DiagnosticSeverity.Warning, true), 
                        Location.None));

                    string valueExpr = _formatter.Format(model.Value.Value, valueTypeSymbol);
                    sb.AppendLine($"            {targetName}.{propertyKey} = {valueExpr};");
                }
            }
            
            return null;
        }

        private string? GenerateNestedControl(
            StringBuilder sb, 
            PropertyModel model, 
            string propertyName, 
            INamedTypeSymbol? targetTypeSymbol)
        {
            string fullTypeName = $"global::{model.Type}";
            string? assignedVarName;
            
            string? controlName = FindControlName(model);

            if (!string.IsNullOrEmpty(controlName)) 
            {
                assignedVarName = $"this.{controlName}"; 
                sb.AppendLine($"            {assignedVarName} = new {fullTypeName}();");
                _context.ReportDiagnostic(Diagnostic.Create(
                    new DiagnosticDescriptor("ADG9915", "Code Gen: Control Init (Field)", $"Initialized field {assignedVarName}.", "Debug", DiagnosticSeverity.Warning, true), 
                    Location.None));
            }
            else
            {
                assignedVarName = $"_gen_{propertyName}_{Guid.NewGuid().ToString().Replace("-", "").Substring(0, 4)}";
                sb.AppendLine($"            {fullTypeName} {assignedVarName} = new {fullTypeName}();");
                _context.ReportDiagnostic(Diagnostic.Create(
                    new DiagnosticDescriptor("ADG9916", "Code Gen: Control Init (Local)", $"Initialized local {assignedVarName}.", "Debug", DiagnosticSeverity.Warning, true), 
                    Location.None));
            }

            var newObjectType = _resolver.ResolveType(model.Type);
            
            if (model.Properties != null) 
            {
                foreach (var innerPropEntry in model.Properties)
                {
                    GeneratePropertyAssignment(
                        sb, 
                        targetName: assignedVarName, 
                        targetTypeSymbol: newObjectType, 
                        propertyName: innerPropEntry.Key, 
                        model: innerPropEntry.Value);
                }
            }
            
            return assignedVarName;
        }
        
        private void HandleAttachedProperty(StringBuilder sb, string targetName, string key, JsonElement value)
        {
            int lastDot = key.LastIndexOf('.');
            string ownerName = key.Substring(0, lastDot);
            string propName = key.Substring(lastDot + 1);

            var ownerType = _resolver.ResolveType(ownerName);
            var setter = ownerType != null ? _resolver.FindAttachedSetter(ownerType, propName) : null;

            if (ownerType != null && setter != null)
            {
                var valType = setter.Parameters[1].Type;
                string valueExpr = _formatter.Format(value, valType);
                sb.AppendLine($"            global::{ownerName}.{setter.Name}({targetName}, {valueExpr});");
            }
            else
            {
                _context.ReportDiagnostic(Diagnostic.Create(
                    new DiagnosticDescriptor("ADG0002", "Attached Property Error", 
                    $"Setter not found for {key}", "Generation", DiagnosticSeverity.Warning, true), Location.None));
            }
        }
        
        private string? FindControlName(PropertyModel model)
        {
            if (model.Properties.TryGetValue("Name", out PropertyModel? nameProp) && nameProp.Value.HasValue)
            {
                if (nameProp.Value.Value.ValueKind == JsonValueKind.String)
                {
                    return nameProp.Value.Value.GetString();
                }
            }
            return null;
        }
    }
}