using System;
using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using ArxisStudio.Markup.Sample.ViewModels;

namespace ArxisStudio.Markup.Sample;

/// <summary>
/// По модели представления возвращает соответствующий визуальный элемент.
/// </summary>
[RequiresUnreferencedCode(
    "Default implementation of ViewLocator involves reflection which may be trimmed away.",
    Url = "https://docs.avaloniaui.net/docs/concepts/view-locator")]
public class ViewLocator : IDataTemplate
{
    /// <summary>
    /// Создаёт контрол для переданного объекта модели представления.
    /// </summary>
    /// <param name="param">Экземпляр модели представления.</param>
    /// <returns>Созданный контрол или <see langword="null"/>.</returns>
    public Control? Build(object? param)
    {
        if (param is null)
            return null;

        var name = param.GetType().FullName!.Replace("ViewModel", "View", StringComparison.Ordinal);
        var type = Type.GetType(name);

        if (type != null)
        {
            return (Control)Activator.CreateInstance(type)!;
        }

        return new TextBlock { Text = "Not Found: " + name };
    }

    /// <summary>
    /// Проверяет, поддерживается ли переданный объект как модель представления.
    /// </summary>
    /// <param name="data">Проверяемый объект.</param>
    /// <returns><see langword="true"/>, если объект является <see cref="ViewModelBase"/>.</returns>
    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}
