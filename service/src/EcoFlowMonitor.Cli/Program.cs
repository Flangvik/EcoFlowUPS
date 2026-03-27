using System.Text.Json;
using EcoFlowMonitor.Client;
using EcoFlowMonitor.Config;
using EcoFlowMonitor.Logging;
using EcoFlowMonitor.Models;
using EcoFlowMonitor.Protocol;
using EcoFlowMonitor.State;

// Load config for credentials
var config = ConfigManager.Load();
if (config.Account == null || string.IsNullOrEmpty(config.Account.Email))
{
    Console.WriteLine("ERROR: No account configured. Run the GUI app and login first.");
    return;
}

Console.WriteLine($"Account: {config.Account.Email}");
Console.WriteLine($"CloudUserId: {config.CloudUserId}");
Console.WriteLine($"Devices: {config.Devices.Count}");
foreach (var d in config.Devices)
    Console.WriteLine($"  {d.DisplayName} sn={d.SerialNumber} mode={d.ConnectionMode} ble={d.HasBle}");

// Pick a cloud device to test MQTT
var device = config.Devices.FirstOrDefault(d => d.HasCloud);
if (device == null)
{
    Console.WriteLine("No cloud device found.");
    return;
}

Console.WriteLine($"\n=== Testing MQTT for: {device.DisplayName} (sn={device.SerialNumber}) ===\n");

// Login and get MQTT creds
using var client = new EcoFlowClient();
await client.LoginAsync(config.Account.Email!, config.Account.Password!);
Console.WriteLine($"Logged in. UserId={client.UserId}");

var creds = await client.GetMqttCredsAsync();
Console.WriteLine($"MQTT creds: {creds.Host}:{creds.Port}");

// Create state and monitor
var state = new DeviceState { DeviceName = device.DisplayName, SerialNumber = device.SerialNumber };
var monitor = new MqttMonitor(device, state, creds, client.UserId!);

int msgCount = 0;
monitor.StateChanged += (s, e) =>
{
    msgCount++;
    var st = e.State;
    Console.WriteLine($"\n--- Message #{msgCount} ---");

    if (st.Bms != null)
    {
        Console.WriteLine("  BMS:");
        Console.WriteLine($"    BatteryPct:    {st.Bms.BatteryPct}");
        Console.WriteLine($"    VoltageV:      {st.Bms.VoltageV}");
        Console.WriteLine($"    CurrentA:      {st.Bms.CurrentA}");
        Console.WriteLine($"    TempC:         {st.Bms.TempC}");
        Console.WriteLine($"    RemainMin:     {st.Bms.RemainMin}");
        Console.WriteLine($"    Cycles:        {st.Bms.Cycles}");
        Console.WriteLine($"    SohPct:        {st.Bms.SohPct}");
        Console.WriteLine($"    InputW:        {st.Bms.InputW}");
        Console.WriteLine($"    OutputW:       {st.Bms.OutputW}");
        Console.WriteLine($"    DesignCapMah:  {st.Bms.DesignCapMah}");
        Console.WriteLine($"    RemainCapMah:  {st.Bms.RemainCapMah}");
        Console.WriteLine($"    MaxCellMv:     {st.Bms.MaxCellMv}");
        Console.WriteLine($"    MinCellMv:     {st.Bms.MinCellMv}");
        Console.WriteLine($"    PackSn:        {st.Bms.PackSn}");
    }

    if (st.Display != null)
    {
        Console.WriteLine("  Display:");
        Console.WriteLine($"    TotalInW:      {st.Display.TotalInW}");
        Console.WriteLine($"    TotalOutW:     {st.Display.TotalOutW}");
        Console.WriteLine($"    AcInW:         {st.Display.AcInW}");
        Console.WriteLine($"    SolarHighW:    {st.Display.SolarInHighW}");
        Console.WriteLine($"    SolarLowW:     {st.Display.SolarInLowW}");
        Console.WriteLine($"    UsbA1W:        {st.Display.UsbA1W}");
        Console.WriteLine($"    UsbA2W:        {st.Display.UsbA2W}");
        Console.WriteLine($"    UsbC1W:        {st.Display.UsbC1W}");
        Console.WriteLine($"    UsbC2W:        {st.Display.UsbC2W}");
        Console.WriteLine($"    AcPluggedIn:   {st.Display.AcPluggedIn}");
        Console.WriteLine($"    AcInFreqHz:    {st.Display.AcInFreqHz}");
    }

    if (st.Ems != null)
    {
        Console.WriteLine("  EMS:");
        Console.WriteLine($"    ChgState:      {st.Ems.ChgState}");
        Console.WriteLine($"    FanLevel:      {st.Ems.FanLevel}");
        Console.WriteLine($"    MaxChargeSoc:  {st.Ems.MaxChargeSoc}");
        Console.WriteLine($"    UpsMode:       {st.Ems.UpsMode}");
        Console.WriteLine($"    ChgRemainMin:  {st.Ems.ChgRemainMin}");
        Console.WriteLine($"    DsgRemainMin:  {st.Ems.DsgRemainMin}");
    }

    Console.WriteLine($"  Power: {st.Power.Status}");
};

await monitor.StartAsync();
Console.WriteLine("MQTT connected. Listening for 30 seconds...\n");
await Task.Delay(30000);
await monitor.StopAsync();

Console.WriteLine($"\n=== Done. Received {msgCount} state updates. ===");
