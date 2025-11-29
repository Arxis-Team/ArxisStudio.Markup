using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia.Controls;
using Newtonsoft.Json;
using JsonUiEditor.Models;
using JsonUiEditor.Services;
using System;
using Avalonia.Media;

namespace JsonUiEditor.ViewModels
{
    public partial class MainWindowViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _jsonText = "";

        [ObservableProperty]
        private Control? _renderedContent;

        [ObservableProperty]
        private string _errorMessage = "";

        // Этот метод вызывается автоматически при изменении JsonText (если используете CommunityToolkit)
        partial void OnJsonTextChanged(string value)
        {
            RenderUi();
        }

        private void RenderUi()
        {
            if (string.IsNullOrWhiteSpace(JsonText)) return;

            try
            {
                ErrorMessage = ""; 

                // 1. Десериализация в корневую модель (RootModel)
                var rootModel = JsonConvert.DeserializeObject<RootModel>(JsonText);

                // Проверка на минимальную корректность
                if (rootModel == null || rootModel.Properties == null) return;
                
                // 2. Получаем корневой контрол из словаря Properties
                if (rootModel.Properties.TryGetValue("Content", out object? contentToken))
                {
                    // Преобразуем объект в ControlModel (через JObject)
                    var jObject = contentToken as Newtonsoft.Json.Linq.JObject;
                    if (jObject == null) 
                    {
                        // Если "Content" - это не JObject, значит структура не соответствует ControlModel
                        throw new InvalidOperationException("The 'Content' property must be a complex object (JObject) describing a control.");
                    }
                    
                    var contentModel = jObject.ToObject<ControlModel>();
                    
                    if (contentModel == null) return;

                    // 3. Построение UI
                    // Создаем временный контейнер для установки свойств Width/Height/Background корневой формы
                    var rootContainer = new Border
                    {
                        Width = rootModel.Properties.ContainsKey("Width") ? Convert.ToDouble(rootModel.Properties["Width"]) : double.NaN,
                        Height = rootModel.Properties.ContainsKey("Height") ? Convert.ToDouble(rootModel.Properties["Height"]) : double.NaN,
                        // Если Background установлен в корневом Properties, применяем его
                        Background = rootModel.Properties.ContainsKey("Background") ? Brush.Parse(rootModel.Properties["Background"].ToString()!) : Brushes.White
                    };
                    
                    // Устанавливаем построенный контент
                    rootContainer.Child = UiBuilder.Build(contentModel);
                    RenderedContent = rootContainer;
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error: {ex.Message}";
            }
        }
        
        public MainWindowViewModel()
        {
            // Ваш предоставленный JSON-пример с градиентом
            JsonText = @"{
  ""FormName"": ""GradientCardControl"",
  ""NamespaceSuffix"": ""Forms"",
  ""ParentClassType"": ""Avalonia.Controls.UserControl"",
  ""Properties"": {
    ""Width"": 800,
    ""Height"": 400,
    ""Background"": ""Black"",
    ""Content"": {
      ""Type"": ""Avalonia.Controls.Border"",
      ""Properties"": {
        ""Width"": 400,
        ""Height"": 300,
        ""Name"": ""GradientBorder"",
        ""BorderThickness"": ""5"",
        ""CornerRadius"": ""20"",
        ""Padding"": ""20"",

        ""BorderBrush"": {
          ""Type"": ""Avalonia.Media.LinearGradientBrush"",
          ""Properties"": {
            ""StartPoint"": ""0%,0%"",
            ""EndPoint"": ""100%,100%"",
            ""GradientStops"": [
              {
                ""Type"": ""Avalonia.Media.GradientStop"",
                ""Properties"": {
                  ""Color"": ""#FFFF0000"",
                  ""Offset"": 0.0
                }
              },
              {
                ""Type"": ""Avalonia.Media.GradientStop"",
                ""Properties"": {
                  ""Color"": ""#FF0000FF"",
                  ""Offset"": 1.0
                }
              }
            ]
          }
        },

        ""Background"": {
          ""Type"": ""Avalonia.Media.SolidColorBrush"",
          ""Properties"": {
            ""Color"": ""#10000000""
          }
        },

        ""Child"": {
          ""Type"": ""Avalonia.Controls.TextBlock"",
          ""Properties"": {
            ""Text"": ""Gradient Test"",
            ""HorizontalAlignment"": ""Center"",
            ""VerticalAlignment"": ""Center"",
            ""FontSize"": 24,
            ""FontWeight"": ""Bold""
          }
        }
      }
    }
  }
}";
            RenderUi();
        }
    }
}