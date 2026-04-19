using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using EcoFlowMonitor.ViewModels.Automation;

namespace EcoFlowMonitor.Views.Automation;

public partial class RuleHistoryWindow : Window
{
    public RuleHistoryWindow()
    {
        InitializeComponent();
        Opened += async (_, _) =>
        {
            if (DataContext is RuleHistoryViewModel vm)
                await vm.RefreshAsync();
        };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
