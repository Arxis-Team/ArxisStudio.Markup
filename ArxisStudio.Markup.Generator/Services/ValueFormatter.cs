using System;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Newtonsoft.Json.Linq;

namespace ArxisStudio.Markup.Generator.Services
{
    internal class ValueFormatter
    {
        private readonly TypeResolver _resolver;

        internal ValueFormatter(TypeResolver resolver)
        {
            _resolver = resolver;
        }

        internal string Format(object element, ITypeSymbol? targetType)
        {
            if (targetType is null || element is null)
                return FormatLegacy(element);

            string targetTypeName = targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            if (targetType.TypeKind == TypeKind.Enum)
                return FormatEnum(element, targetType);

            if (element is string s)
            {
                if (IsNumeric(targetType)) return s;
                string escapedString = Escape(s);
                if (IsBrush(targetType)) return $"global::Avalonia.Media.Brush.Parse(\"{escapedString}\")";
                if (HasStaticParseMethod(targetType)) return $"{targetTypeName}.Parse(\"{escapedString}\")";
                return $"\"{escapedString}\"";
            }

            if (targetType.SpecialType == SpecialType.System_Boolean && element is bool b)
                return b ? "true" : "false";

            if (IsNumeric(targetType))
                return Convert.ToString(element, CultureInfo.InvariantCulture) ?? "0";

            if (element is JToken token)
            {
                if (targetType.TypeKind == TypeKind.Enum) return FormatEnum(token.ToString(), targetType);
                if (token.Type == JTokenType.String)
                {
                    string ts = token.ToString();
                    if (IsNumeric(targetType)) return ts;
                    string escaped = Escape(ts);
                    if (IsBrush(targetType)) return $"global::Avalonia.Media.Brush.Parse(\"{escaped}\")";
                    if (HasStaticParseMethod(targetType)) return $"{targetTypeName}.Parse(\"{escaped}\")";
                    return $"\"{escaped}\"";
                }
                if (IsNumeric(targetType)) return Convert.ToString(token, CultureInfo.InvariantCulture) ?? "0";
            }

            return FormatLegacy(element);
        }

        private string Escape(string s)
            => s.Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");

        private string FormatEnum(object el, ITypeSymbol type)
        {
            string s = el.ToString();
            return $"{type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}.{s}";
        }

        private bool IsNumeric(ITypeSymbol type)
            => type.SpecialType is SpecialType.System_SByte or SpecialType.System_Byte or
                                   SpecialType.System_Int16 or SpecialType.System_UInt16 or
                                   SpecialType.System_Int32 or SpecialType.System_UInt32 or
                                   SpecialType.System_Int64 or SpecialType.System_UInt64 or
                                   SpecialType.System_Single or SpecialType.System_Double or
                                   SpecialType.System_Decimal;

        private string FormatLegacy(object? element)
        {
            if (element is string s) return $"\"{Escape(s)}\"";
            if (element is bool b) return b ? "true" : "false";
            if (element is IFormattable f) return f.ToString(null, CultureInfo.InvariantCulture);
            return element?.ToString() ?? "null";
        }

        private bool HasStaticParseMethod(ITypeSymbol type)
        {
            return type.GetMembers("Parse").OfType<IMethodSymbol>().Any(m =>
                    m.IsStatic && m.DeclaredAccessibility == Accessibility.Public &&
                    m.Parameters.Length == 1 && m.Parameters[0].Type.SpecialType == SpecialType.System_String);
        }

        private bool IsBrush(ITypeSymbol type)
        {
            var targetType = _resolver.ResolveType("Avalonia.Media.IBrush");
            if (targetType == null) return false;
            return SymbolEqualityComparer.Default.Equals(type, targetType) ||
                   type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, targetType));
        }
    }
}
