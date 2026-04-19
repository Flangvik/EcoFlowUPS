using Avalonia.Controls;
using EcoFlowMonitor.ViewModels;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace EcoFlowMonitor.Controls;

public partial class PowerHistoryChart : UserControl
{
    public PowerHistoryChart()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is DeviceViewModel vm)
        {
            UpdateChart(vm);
        }
    }

    private void UpdateChart(DeviceViewModel vm)
    {
        var inputValues = vm.PowerHistory.Select(p => (double)p.InputW).ToArray();
        var outputValues = vm.PowerHistory.Select(p => (double)p.OutputW).ToArray();

        Chart.Series = new ISeries[]
        {
            new LineSeries<double>
            {
                Values = inputValues,
                Name = "Input",
                Stroke = new SolidColorPaint(SKColor.Parse("#00E676")) { StrokeThickness = 2 },
                Fill = new SolidColorPaint(SKColor.Parse("#1A00E676")),
                GeometrySize = 0,
                LineSmoothness = 0.5
            },
            new LineSeries<double>
            {
                Values = outputValues,
                Name = "Output",
                Stroke = new SolidColorPaint(SKColor.Parse("#FF9100")) { StrokeThickness = 2 },
                Fill = new SolidColorPaint(SKColor.Parse("#1AFF9100")),
                GeometrySize = 0,
                LineSmoothness = 0.5
            }
        };

        Chart.XAxes = new Axis[]
        {
            new Axis
            {
                IsVisible = false,
                ShowSeparatorLines = false
            }
        };

        Chart.YAxes = new Axis[]
        {
            new Axis
            {
                LabelsPaint = new SolidColorPaint(SKColor.Parse("#888888")) { StrokeThickness = 1 },
                SeparatorsPaint = new SolidColorPaint(SKColor.Parse("#2A2A2A")) { StrokeThickness = 1 },
                MinLimit = 0
            }
        };
    }
}
