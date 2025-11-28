using System.Linq;
using Microsoft.CodeAnalysis;
using Newtonsoft.Json.Linq;

namespace AvaloniaDesigner.Generator.Services
{
    /// <summary>
    /// Отвечает за генерацию C# кода для значений свойств (строки, числа, Parse, Enums).
    /// </summary>
    public class ValueFormatter
    {
        private readonly TypeResolver _resolver;

        public ValueFormatter(TypeResolver resolver)
        {
            _resolver = resolver;
        }

        public string Format(object element, ITypeSymbol? targetType)
        {
            if (targetType is null || element is null)
                return FormatLegacy(element);

            string targetTypeName = targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            // --- 0. СНАЧАЛА: ENUM ---
            if (targetType.TypeKind == TypeKind.Enum)
            {
                return FormatEnum(element, targetType);
            }

            // --- 1. ОБРАБОТКА ПРИМИТИВОВ (полученных напрямую от Newtonsoft.Json) ---

            if (element is string s)
            {
                // 🔹 ЧИСЛОВЫЕ ТИПЫ: генерируем литерал, а не Parse(...)
                if (IsNumeric(targetType))
                {
                    // JSON по спецификации уже использует '.', так что это валидный C# литерал
                    // "0.5" → 0.5   /  "10" → 10
                    return s;
                }

                string escapedString = Escape(s);

                // Кисти — через Brush.Parse(...)
                if (IsBrush(targetType))
                    return $"global::Avalonia.Media.Brush.Parse(\"{escapedString}\")";

                // Общий случай для типов со статическим Parse(string)
                if (HasStaticParseMethod(targetType))
                    return $"{targetTypeName}.Parse(\"{escapedString}\")";

                // Обычная строка
                return $"\"{escapedString}\"";
            }

            if (targetType.SpecialType == SpecialType.System_Boolean && element is bool b)
                return b ? "true" : "false";

            // Если это уже число (int, double и т.п.) — просто выводим как есть
            if (IsNumeric(targetType))
            {
                return element.ToString() ?? "0";
            }

            // --- 2. ОБРАБОТКА JToken (если Newtonsoft.Json не десериализовал в примитив) ---
            if (element is JToken token)
            {
                if (targetType.TypeKind == TypeKind.Enum)
                {
                    return FormatEnum(token.ToString(), targetType);
                }

                if (token.Type == JTokenType.String)
                {
                    string tokenString = token.ToString();

                    // Для числовых типов строковый токен также трактуем как литерал
                    if (IsNumeric(targetType))
                    {
                        return tokenString;
                    }

                    string escapedString = Escape(tokenString);

                    if (IsBrush(targetType))
                        return $"global::Avalonia.Media.Brush.Parse(\"{escapedString}\")";

                    if (HasStaticParseMethod(targetType))
                        return $"{targetTypeName}.Parse(\"{escapedString}\")";

                    return $"\"{escapedString}\"";
                }

                // Числа/другое — отдадим как есть (будет валидный литерал)
                return token.ToString();
            }

            // --- 3. Фоллбек ---
            return FormatLegacy(element);
        }

        // =================================================================
        //                      ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ
        // =================================================================

        private string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

        private string FormatEnum(object el, ITypeSymbol type)
        {
            if (el is string s)
                return $"{type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}.{s}";

            return el.ToString() ?? "0";
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
            return element?.ToString() ?? "null";
        }

        private bool HasStaticParseMethod(ITypeSymbol type)
        {
            return type.GetMembers("Parse")
                .OfType<IMethodSymbol>()
                .Any(m =>
                    m.IsStatic &&
                    m.DeclaredAccessibility == Accessibility.Public &&
                    m.Parameters.Length == 1 &&
                    m.Parameters[0].Type.SpecialType == SpecialType.System_String
                );
        }

        private bool IsBrush(ITypeSymbol type)
        {
            return IsAssignableTo(type, "Avalonia.Media.IBrush") ||
                   IsAssignableTo(type, "Avalonia.Media.Brush");
        }

        private bool IsAssignableTo(ITypeSymbol type, string fullName)
        {
            var targetType = _resolver.ResolveType(fullName);
            if (targetType == null) return false;

            return SymbolEqualityComparer.Default.Equals(type, targetType) ||
                   type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, targetType));
        }
    }
}
