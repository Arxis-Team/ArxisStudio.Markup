// ArxisStudio.Markup.Json.Loader/Views/MainWindow.axaml.cs

using Avalonia.Controls;
using ArxisStudio.Markup.Json.Loader.ViewModels;
using Newtonsoft.Json.Linq;

namespace ArxisStudio.Markup.Json.Loader.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }
        
        // Обработчик события выбора элемента из Toolbox
        private void ToolboxListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (sender is ListBox listBox && listBox.SelectedItem is JObject selectedTemplate)
            {
                if (DataContext is MainWindowViewModel viewModel)
                {
                    // Вызываем команду добавления, передавая JSON-шаблон
                    viewModel.AddNewControlCommand.Execute(selectedTemplate);
                }
                
                // Сбрасываем выделение, чтобы можно было добавить еще один
                listBox.SelectedItem = null; 
            }
        }
    }
}