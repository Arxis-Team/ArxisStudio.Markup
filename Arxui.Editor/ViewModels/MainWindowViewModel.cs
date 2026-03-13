using ArxisStudio.Markup.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Controls;
using ArxisStudio.Markup.Json.Loader.Services;
using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using System.Collections.ObjectModel;
using ArxisStudio.Markup.Json.Loader.Models;

namespace ArxisStudio.Markup.Json.Loader.ViewModels
{
    public partial class MainWindowViewModel : ObservableObject
    {
        private bool _suppressJsonTextChanged;

        [ObservableProperty]
        private string _jsonText = "";

        [ObservableProperty]
        private Control? _renderedContent;

        [ObservableProperty]
        private string _errorMessage = "";
        
        // --- Свойства конструктора ---
        
        [ObservableProperty]
        private ControlNode? _controlTreeRoot;
        
        [ObservableProperty]
        private ControlNode? _selectedNode; 
        
        [ObservableProperty]
        private ObservableCollection<PropertyItem> _editableProperties = new();

        // Коллекция доступных контролов (для Toolbox)
        public ObservableCollection<JObject> AvailableControls { get; } = new()
        {
            new JObject
            {
                { "TypeName", "Avalonia.Controls.Button" },
                { "Properties", new JObject 
                    {
                        { "Content", "New Button" },
                        { "Width", 100 } 
                    }
                }
            },
            new JObject
            {
                { "TypeName", "Avalonia.Controls.StackPanel" },
                { "Properties", new JObject 
                    {
                        { "Orientation", "Vertical" },
                        { "Children", new JArray() } 
                    }
                }
            },
            new JObject
            {
                { "TypeName", "Avalonia.Controls.TextBlock" },
                { "Properties", new JObject 
                    {
                        { "Text", "New TextBlock" },
                        { "FontSize", 16 }
                    }
                }
            },
            new JObject
            {
                { "TypeName", "Avalonia.Controls.Border" },
                { "Properties", new JObject 
                    {
                        { "Width", 150 },
                        { "Height", 50 },
                        { "BorderBrush", "Gray" },
                        { "BorderThickness", 1 }
                    }
                }
            }
        };

        public MainWindowViewModel()
        {
            JsonText = @"{
  ""SchemaVersion"": 1,
  ""Kind"": ""UserControl"",
  ""Root"": {
    ""TypeName"": ""Avalonia.Controls.UserControl"",
    ""Properties"": {
      ""Width"": 600,
      ""Height"": 600,
      ""Content"": {
        ""TypeName"": ""Avalonia.Controls.Canvas"",
        ""Properties"": {
          ""Width"": 400,
          ""Height"": 400,
          ""Background"": ""Black"",
          ""Children"": [
            {
              ""TypeName"": ""Avalonia.Controls.Shapes.Rectangle"",
              ""Properties"": {
                ""Width"": 100,
                ""Height"": 100,
                ""Fill"": ""Red"",
                ""Avalonia.Controls.Canvas.Left"": 50,
                ""Avalonia.Controls.Canvas.Top"": 50
              }
            },
            {
              ""TypeName"": ""Avalonia.Controls.Shapes.Ellipse"",
              ""Properties"": {
                ""Width"": 80,
                ""Height"": 80,
                ""Fill"": ""Blue"",
                ""Stroke"": ""White"",
                ""StrokeThickness"": 2,
                ""Avalonia.Controls.Canvas.Left"": 200,
                ""Avalonia.Controls.Canvas.Top"": 100
              }
            }
          ]
        }
      }
    }
  }
}";
            DesignerService.Instance.JsonChanged += OnJsonChanged;
            UpdateTreeAndUIFromText();
        }

        private void OnJsonChanged(DesignerChangeKind changeKind)
        {
            UpdateUIFromRootModel(changeKind == DesignerChangeKind.Structure);
        }

        partial void OnJsonTextChanged(string value)
        {
            if (_suppressJsonTextChanged)
            {
                return;
            }

            UpdateTreeAndUIFromText();
        }

        partial void OnSelectedNodeChanged(ControlNode? oldValue, ControlNode? newValue)
        {
            LoadProperties(newValue);
            // Явно оповещаем команду об изменении SelectedNode
            DeleteSelectedControlCommand.NotifyCanExecuteChanged(); 
        }
        
        // --- Основная логика синхронизации и исправления ошибки ---

        private void UpdateTreeAndUIFromText()
        {
            try
            {
                ErrorMessage = "";
                var rootJson = JObject.Parse(JsonText);
                
                // Это первый запуск, просто строим и обновляем UI
                ControlTreeRoot = TreeBuilder.BuildTree(rootJson);

                UpdateUIFromRootModel();
            }
            catch (Exception ex)
            {
                ErrorMessage = $"JSON Parse Error: {ex.Message}";
                ControlTreeRoot = null;
            }
        }

        private void UpdateUIFromRootModel(bool rebuildTree = true)
        {
            if (ControlTreeRoot?.ParentJsonContainer == null) return;
            
            // 1. СОХРАНЯЕМ JSON-ссылку текущего выделенного узла
            JObject? selectedJson = SelectedNode?.JsonNode;
            
            try
            {
                var rootJson = (JObject)ControlTreeRoot.ParentJsonContainer;
                var rootModel = ArxuiSerializer.Deserialize(rootJson.ToString());
                
                if (rootModel == null) return;

                // 2. Обновляем JsonText (СИНХРОНИЗАЦИЯ)
                _suppressJsonTextChanged = true;
                try
                {
                    JsonText = ArxuiSerializer.Serialize(rootModel);
                }
                finally
                {
                    _suppressJsonTextChanged = false;
                }

                // 3. Строим UI (Рендеринг)
                if (rootModel.Root.Properties.TryGetValue("Content", out var contentValue) &&
                    contentValue is NodeValue contentNode)
                {
                    var builtControl = UiBuilder.Build(contentNode.Node);

                    // Обертывание в корневой контейнер
                    var rootContainer = new Border
                    {
                        Width = GetScalarDouble(rootModel.Root.Properties, "Width"),
                        Height = GetScalarDouble(rootModel.Root.Properties, "Height"),
                        Child = builtControl
                    };
                    RenderedContent = rootContainer;
                }
                
                if (!rebuildTree)
                {
                    return;
                }

                // 4. ПЕРЕСТРАИВАЕМ ДЕРЕВО: Это создает НОВЫЕ ControlNode объекты
                ControlTreeRoot = TreeBuilder.BuildTree(rootJson);
                
                // 5. ВОССТАНОВЛЕНИЕ: Ищем предыдущий выделенный JSON-объект в новом дереве
                if (selectedJson != null)
                {
                    var newlySelectedNode = FindNodeByJson(ControlTreeRoot, selectedJson);
                    
                    if (newlySelectedNode != null)
                    {
                        SelectedNode = newlySelectedNode;
                        // Принудительно уведомляем View о том, что SelectedNode изменился,
                        // чтобы TreeView обновил выделение
                        OnPropertyChanged(nameof(SelectedNode));
                    }
                }
                
                // 6. Перезагружаем свойства для текущего узла (который теперь восстановлен)
                LoadProperties(SelectedNode);
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Rendering Error: {ex.Message}";
            }
        }

        private static double GetScalarDouble(
            System.Collections.Generic.IReadOnlyDictionary<string, UiValue> properties,
            string propertyName)
        {
            if (!properties.TryGetValue(propertyName, out var property) ||
                property is not ScalarValue scalar ||
                scalar.Value == null)
            {
                return double.NaN;
            }

            return Convert.ToDouble(scalar.Value);
        }

        // Вспомогательный метод: ищет узел в дереве по ссылке на исходный JObject
        private ControlNode? FindNodeByJson(ControlNode? root, JObject targetJson)
        {
            if (root == null) return null;

            // Сравнение по ссылке (так как JObject — это объект-ссылка)
            if (root.JsonNode == targetJson)
            {
                return root;
            }

            // Рекурсивный поиск
            foreach (var child in root.Children)
            {
                var found = FindNodeByJson(child, targetJson);
                if (found != null)
                {
                    return found;
                }
            }
            
            return null;
        }


        private void LoadProperties(ControlNode? node)
        {
            EditableProperties.Clear();

            if (node?.JsonNode.TryGetValue("Properties", out JToken? propertiesToken) == true && propertiesToken is JObject propertiesObject)
            {
                foreach (var prop in propertiesObject.Properties())
                {
                    if (prop.Value is JValue jValue)
                    {
                        var item = new PropertyItem(
                            prop.Name, 
                            jValue, 
                            propertiesObject
                        );
                        EditableProperties.Add(item);
                    }
                }
            }
        }
        
        // --- Команды конструктора ---

        [RelayCommand]
        private void AddNewControl(JObject? controlTemplate)
        {
            if (SelectedNode == null)
            {
                ErrorMessage = "Please select a container control (e.g., StackPanel, Grid) first.";
                return;
            }
            
            if (controlTemplate == null) return;

            JObject newControlJson = (JObject)controlTemplate.DeepClone();

            if (!SelectedNode.JsonNode.TryGetValue("Properties", out JToken? propertiesToken) || propertiesToken is not JObject propertiesObject)
            {
                ErrorMessage = $"Selected node {SelectedNode.DisplayName} does not have a 'Properties' object.";
                return;
            }

            JArray? childrenArray = propertiesObject.Properties()
                                                  .Where(p => p.Value is JArray)
                                                  .Select(p => p.Value as JArray)
                                                  .FirstOrDefault();

            if (childrenArray == null)
            {
                ErrorMessage = $"Selected node '{SelectedNode.DisplayName}' does not have a suitable JArray (e.g., 'Children') property to add controls.";
                return;
            }

            childrenArray.Add(newControlJson);
            DesignerService.Instance.NotifyJsonChanged(DesignerChangeKind.Structure);
            ErrorMessage = "";
        }

        [RelayCommand(CanExecute = nameof(CanDeleteSelectedControl))]
        private void DeleteSelectedControl()
        {
            if (SelectedNode == null || SelectedNode.ParentJsonContainer == null) return;

            if (SelectedNode.ParentJsonContainer is JArray parentArray)
            {
                parentArray.Remove(SelectedNode.JsonNode);
            }
            else if (SelectedNode.ParentJsonContainer is JObject parentObject)
            {
                var propertyToRemove = parentObject.Properties()
                                                 .FirstOrDefault(p => p.Value == SelectedNode.JsonNode);
                
                propertyToRemove?.Remove();
            }

            SelectedNode = null;
            DesignerService.Instance.NotifyJsonChanged(DesignerChangeKind.Structure);
            ErrorMessage = "";
        }

        public bool CanDeleteSelectedControl => SelectedNode != null && 
                                               SelectedNode.ParentJsonContainer != null && 
                                               SelectedNode.DisplayName != "Root Document";
    }
}
