using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace ArxisStudio.Markup.Generator.Tests
{
    internal sealed class InMemoryAdditionalText : AdditionalText
    {
        private readonly string _text;

        public InMemoryAdditionalText(string path, string text)
        {
            Path = path;
            _text = text;
        }

        public override string Path { get; }

        public override SourceText? GetText(System.Threading.CancellationToken cancellationToken = default)
            => SourceText.From(_text, Encoding.UTF8);
    }
}