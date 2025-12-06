using System.Linq;
using Microsoft.CodeAnalysis;

namespace AvaloniaDesigner.Generator.Services
{
    public class TypeResolver
    {
        private readonly Compilation _compilation;

        public TypeResolver(Compilation compilation)
        {
            _compilation = compilation;
        }

        public INamedTypeSymbol? ResolveType(string metadataName) 
            => _compilation.GetTypeByMetadataName(metadataName);

        public IPropertySymbol? FindProperty(INamedTypeSymbol type, string propertyName)
        {
            for (var t = type; t is not null; t = t.BaseType)
            {
                foreach (var member in t.GetMembers(propertyName).OfType<IPropertySymbol>()) return member;
            }
            return null;
        }

        public IEventSymbol? FindEvent(INamedTypeSymbol type, string eventName)
        {
            for (var t = type; t is not null; t = t.BaseType)
            {
                foreach (var member in t.GetMembers(eventName).OfType<IEventSymbol>()) return member;
            }
            return null;
        }

        public IFieldSymbol? FindAvaloniaPropertyField(INamedTypeSymbol type, string propertyName)
        {
            string fieldName = propertyName + "Property";
            for (var t = type; t is not null; t = t.BaseType)
            {
                foreach (var member in t.GetMembers(fieldName).OfType<IFieldSymbol>()) 
                    if (member.IsStatic) return member;
            }
            return null;
        }

        public IMethodSymbol? FindAttachedSetter(INamedTypeSymbol ownerType, string attachedName)
        {
            string methodName = "Set" + attachedName;
            foreach (var member in ownerType.GetMembers(methodName))
            {
                if (member is IMethodSymbol m && m.IsStatic && m.Parameters.Length == 2)
                    return m;
            }
            return null;
        }

        public bool IsAssignableTo(ITypeSymbol type, string baseTypeName)
        {
            var baseType = _compilation.GetTypeByMetadataName(baseTypeName);
            if (baseType == null) return false;

            if (SymbolEqualityComparer.Default.Equals(type, baseType)) return true;

            if (type is INamedTypeSymbol namedType)
            {
                if (namedType.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, baseType))) return true;
                
                var t = type.BaseType;
                while (t != null)
                {
                    if (SymbolEqualityComparer.Default.Equals(t, baseType)) return true;
                    t = t.BaseType;
                }
            }
            return false;
        }

        public bool IsCollectionType(ITypeSymbol type)
        {
            if (type is IArrayTypeSymbol) return true;

            var iCol = _compilation.GetTypeByMetadataName("System.Collections.Generic.ICollection`1");
            if (iCol != null && ImplementsInterface(type, iCol)) return true;

            var iListGeneric = _compilation.GetTypeByMetadataName("System.Collections.Generic.IList`1");
            if (iListGeneric != null && ImplementsInterface(type, iListGeneric)) return true;
            
            var iColNonGeneric = _compilation.GetTypeByMetadataName("System.Collections.ICollection");
            if (iColNonGeneric != null && ImplementsInterface(type, iColNonGeneric)) return true;

            var iListNonGeneric = _compilation.GetTypeByMetadataName("System.Collections.IList");
            if (iListNonGeneric != null && ImplementsInterface(type, iListNonGeneric)) return true;

            if (type.GetMembers("Add").OfType<IMethodSymbol>().Any(m => 
                m.DeclaredAccessibility == Accessibility.Public && m.Parameters.Length == 1)) return true;

            return false;
        }
        
        private static bool ImplementsInterface(ITypeSymbol type, INamedTypeSymbol interfaceSymbol)
        {
            if (type is INamedTypeSymbol namedType)
            {
                if (SymbolEqualityComparer.Default.Equals(namedType.ConstructedFrom, interfaceSymbol)) return true;
                return namedType.AllInterfaces.Any(i => 
                    SymbolEqualityComparer.Default.Equals(i.ConstructedFrom, interfaceSymbol) ||
                    (i.IsGenericType && SymbolEqualityComparer.Default.Equals(i.ConstructedFrom, interfaceSymbol)));
            }
            return false;
        }
    }
}