using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EcoFlowMonitor.History;
using EcoFlowMonitor.Services;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace EcoFlowMonitor.ViewModels;

public partial class HistoryViewModel : ViewModelBase
{
    private readonly IHistoryStore _history;
    private readonly IEventStore _events;
    private readonly NavigationService _navigation;
    private string? _currentDeviceSn;

    [ObservableProperty] private string _selectedRange = "24H";
    [ObservableProperty] private bool _isLoading;

    public ObservableCollection<ISeries> BatterySeries { get; } = new();
    public ObservableCollection<ISeries> PowerSeries   { get; } = new();
    public ObservableCollection<Axis>    XAxes         { get; } = new();
    public ObservableCollection<PowerEventItem> EventLog { get; } = new();

    private readonly LineSeries<double> _battLine;
    private readonly LineSeries<double> _powerInLine;
    private readonly LineSeries<double> _powerOutLine;

    public HistoryViewModel(IHistoryStore history, IEventStore events, NavigationService navigation)
    {
        _history    = history;
        _events     = events;
        _navigation = navigation;

        _battLine = new LineSeries<double>
        {
            Values         = new ObservableCollection<double>(),
            Name           = "Battery %",
            Stroke         = new SolidColorPaint(SKColors.Teal) { StrokeThickness = 2 },
            Fill           = null,
            GeometrySize   = 0,
            LineSmoothness = 0.5
        };
        _powerInLine = new LineSeries<double>
        {
            Values         = new ObservableCollection<double>(),
            Name           = "In (W)",
            Stroke         = new SolidColorPaint(new SKColor(0x00, 0xD4, 0xAA)) { StrokeThickness = 2 },
            Fill           = null,
            GeometrySize   = 0,
            LineSmoothness = 0
        };
        _powerOutLine = new LineSeries<double>
        {
            Values         = new ObservableCollection<double>(),
            Name           = "Out (W)",
            Stroke         = new SolidColorPaint(new SKColor(0xFF, 0xB3, 0x00)) { StrokeThickness = 2 },
            Fill           = null,
            GeometrySize   = 0,
            LineSmoothness = 0
        };

        BatterySeries.Add(_battLine);
        PowerSeries.Add(_powerInLine);
        PowerSeries.Add(_powerOutLine);
        XAxes.Add(new Axis { Labels = Array.Empty<string>(), LabelsRotation = -45, TextSize = 10 });
    }

    [RelayCommand]
    private async Task LoadHistoryAsync()
    {
        if (string.IsNullOrEmpty(_currentDeviceSn)) return;
        IsLoading = true;
        try
        {
            var (from, to, resolution) = GetRange();
            var snapshots = await _history.QueryAsync(_currentDeviceSn, from, to, resolution).ConfigureAwait(false);
            var events    = await _events.QueryAsync(_currentDeviceSn, from, to).ConfigureAwait(false);

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                var battVals = (ObservableCollection<double>)_battLine.Values!;
                var inVals   = (ObservableCollection<double>)_powerInLine.Values!;
                var outVals  = (ObservableCollection<double>)_powerOutLine.Values!;
                var labels   = new List<string>(snapshots.Count);

                battVals.Clear(); inVals.Clear(); outVals.Clear();

                foreach (var s in snapshots)
                {
                    battVals.Add(s.BatteryPct ?? 0);
                    inVals.Add(s.TotalInW ?? 0);
                    outVals.Add(s.TotalOutW ?? 0);
                    labels.Add(DateTimeOffset.FromUnixTimeSeconds(s.Ts).LocalDateTime.ToString(
                        resolution == Resolution.Raw ? "HH:mm" : "MM/dd HH:mm"));
                }

                XAxes[0] = new Axis { Labels = labels, LabelsRotation = -45, TextSize = 10 };

                EventLog.Clear();
                foreach (var e in events)
                    EventLog.Add(e);
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"HistoryViewModel.LoadHistoryAsync: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private (DateTimeOffset from, DateTimeOffset to, Resolution resolution) GetRange()
    {
        var now = DateTimeOffset.UtcNow;
        return SelectedRange switch
        {
            "1H"  => (now.AddHours(-1),  now, Resolution.Raw),
            "24H" => (now.AddHours(-24), now, Resolution.Hourly),
            "7D"  => (now.AddDays(-7),   now, Resolution.Hourly),
            "30D" => (now.AddDays(-30),  now, Resolution.Daily),
            _     => (now.AddHours(-24), now, Resolution.Hourly)
        };
    }

    partial void OnSelectedRangeChanged(string value) => LoadHistoryCommand.Execute(null);

    public void SetDevice(string deviceSn)
    {
        _currentDeviceSn = deviceSn;
        LoadHistoryCommand.Execute(null);
    }

    [RelayCommand]
    private void GoBack() => _navigation.NavigateTo(
        App.Services!.GetRequiredService<DashboardViewModel>());
}
