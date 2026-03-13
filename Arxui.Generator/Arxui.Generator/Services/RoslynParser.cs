using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ArxisStudio.Markup.Json.Generator.Services
{
    public class ClassInfo
    {
        public string ClassName { get; }
        public string Namespace { get; }
        public string? BaseClass { get; }

        public ClassInfo(string className, string ns, string? baseClass)
        {
            ClassName = className;
            Namespace = ns;
            BaseClass = baseClass;
        }
    }

    public static class RoslynParser
    {
        public static ClassInfo? ParseClassInfo(SyntaxTree tree)
        {
            var root = tree.GetRoot();
            
            var classDecl = root.DescendantNodes().OfType<ClassDeclarationSyntax>().FirstOrDefault();
            if (classDecl == null) return null;

            string className = classDecl.Identifier.Text;
            string ns = GetNamespace(classDecl);
            
            string? baseClass = null;
            if (classDecl.BaseList != null && classDecl.BaseList.Types.Count > 0)
            {
                baseClass = classDecl.BaseList.Types[0].Type.ToString();
            }

            return new ClassInfo(className, ns, baseClass);
        }

        private static string GetNamespace(SyntaxNode syntax)
        {
            if (syntax.Parent is FileScopedNamespaceDeclarationSyntax fileScoped)
                return fileScoped.Name.ToString();
            
            if (syntax.Parent is NamespaceDeclarationSyntax blockScoped)
                return blockScoped.Name.ToString();

            if (syntax.Parent != null)
                return GetNamespace(syntax.Parent);

            return "Global";
        }
    }
}