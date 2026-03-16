// ArxisStudio.Editor/Views/MainWindow.axaml.cs

using Avalonia.Controls;
using Avalonia.Input;
using ArxisStudio.Editor.Models;
using ArxisStudio.Editor.ViewModels;
using ArxisStudio.Markup.Json.Loader.Models;
using Newtonsoft.Json.Linq;

namespace ArxisStudio.Editor.Views
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
            if (sender is ListBox listBox && listBox.SelectedItem is ToolboxItem selectedItem)
            {
                if (DataContext is MainWindowViewModel viewModel)
                {
                    viewModel.AddNewControlCommand.Execute(selectedItem.Template);
                }
                
                // Сбрасываем выделение, чтобы можно было добавить еще один
                listBox.SelectedItem = null; 
            }
        }

        private void ProjectFilesListBox_DoubleTapped(object? sender, TappedEventArgs e)
        {
            if (sender is not ListBox listBox || listBox.SelectedItem is not ProjectFileItem)
            {
                return;
            }

            if (DataContext is MainWindowViewModel viewModel &&
                viewModel.OpenSelectedProjectFileCommand.CanExecute(null))
            {
                viewModel.OpenSelectedProjectFileCommand.Execute(null);
            }
        }
    }
}
