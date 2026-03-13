using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace ArxisStudio.Markup.Sample.Views;

public partial class TestUserControl : UserControl
{
    public TestUserControl()
    {
        InitializeComponent();
        
        Ellipse border = new();

        ColumnDefinitions.Parse("");
        RowDefinitions.Parse("");
        
            //var bitmap = new Bitmap(AssetLoader.Open(new Uri(uri)));
    }
}