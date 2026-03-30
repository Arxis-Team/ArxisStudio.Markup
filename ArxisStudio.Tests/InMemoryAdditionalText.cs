using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace ArxisStudio.Markup.Generator.Tests
{
    internal sealed class InMemoryAdditionalText : AdditionalText
    {
        private readonly string _text;

        /// <summary>
        /// Инициализирует in-memory реализацию <see cref="AdditionalText"/>.
        /// </summary>
        /// <param name="path">Путь файла, видимый генератору.</param>
        /// <param name="text">Содержимое файла.</param>
        public InMemoryAdditionalText(string path, string text)
        {
            Path = path;
            _text = text;
        }

        /// <summary>
        /// Получает путь файла.
        /// </summary>
        public override string Path { get; }

        /// <summary>
        /// Возвращает содержимое дополнительного файла.
        /// </summary>
        /// <param name="cancellationToken">Токен отмены.</param>
        /// <returns>Текст файла.</returns>
        public override SourceText? GetText(System.Threading.CancellationToken cancellationToken = default)
            => SourceText.From(_text, Encoding.UTF8);
    }
}
