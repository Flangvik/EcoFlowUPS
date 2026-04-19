using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using EcoFlowMonitor.ViewModels.Automation;

namespace EcoFlowMonitor.Views.Automation;

public partial class RuleEditorWindow : Window
{
    public bool Saved { get; private set; }

    public RuleEditorWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Saved = false;
        Close();
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if (DataContext is RuleEditorViewModel vm)
        {
            vm.Save();
            Saved = true;
        }
        Close();
    }
}
