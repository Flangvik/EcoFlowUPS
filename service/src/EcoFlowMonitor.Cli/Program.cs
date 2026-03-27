using System.Text;
using EcoFlowMonitor.Client;
using EcoFlowMonitor.Config;
using EcoFlowMonitor.Protocol;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Formatter;

var config = ConfigManager.Load();
if (config.Account == null) { Console.WriteLine("No account"); return; }

using var api = new EcoFlowClient();
await api.LoginAsync(config.Account.Email!, config.Account.Password!);
var creds = await api.GetMqttCredsAsync();

var topics = config.Devices.Select(d => d.SerialNumber!).Distinct().ToList();
var clientId = $"ANDROID_{Guid.NewGuid().ToString().ToUpper()}_{api.UserId}";
var options = new MqttClientOptionsBuilder()
    .WithClientId(clientId)
    .WithTcpServer(creds.Host, creds.Port)
    .WithCredentials(creds.Username, creds.Password)
    .WithProtocolVersion(MqttProtocolVersion.V311)
    .WithTlsOptions(o => { o.UseTls(); o.WithCertificateValidationHandler(_ => true); })
    .WithCleanSession().Build();

var client = new MqttFactory().CreateMqttClient();
var seen = new HashSet<string>();

// Focus on cmdId=22 raw fields — what extra data does it contain?
client.ApplicationMessageReceivedAsync += e =>
{
    var seg = e.ApplicationMessage.PayloadSegment;
    if (seg.Count == 0) return Task.CompletedTask;
    byte[] raw = new byte[seg.Count];
    Array.Copy(seg.Array!, seg.Offset, raw, 0, seg.Count);

    try
    {
        var (pdata, cmdFunc, cmdId, encType, seq) = ProtobufDecoder.ParseOuter(raw);
        var sn = e.ApplicationMessage.Topic?.Split('/').LastOrDefault() ?? "?";
        var key = $"{sn}:{cmdFunc}:{cmdId}";

        // Only dump each unique message type once
        if (!seen.Add(key)) return Task.CompletedTask;

        var fields = ProtobufDecoder.DecodeFields(pdata);
        Console.WriteLine($"\n{'='*65}");
        Console.WriteLine($"  {sn}  cmdFunc={cmdFunc} cmdId={cmdId}  {pdata.Length} bytes  {fields.Count} fields");
        Console.WriteLine($"{'='*65}");

        // Dump ALL fields with types
        foreach (var (fnum, vals) in fields.OrderBy(kv => kv.Key))
        {
            foreach (var v in vals)
            {
                if (v is ulong u)
                {
                    long signed = (long)u;
                    if (u > (ulong)long.MaxValue) signed = unchecked((long)u);
                    Console.WriteLine($"    f{fnum,4}: {u,12} (signed: {signed,12})  hex: 0x{u:X}");
                }
                else if (v is byte[] b && b.Length == 4)
                {
                    var f32 = BitConverter.ToSingle(b, 0);
                    Console.WriteLine($"    f{fnum,4}: float32 = {f32:F4}  bytes: {Convert.ToHexString(b)}");
                }
                else if (v is byte[] b2)
                {
                    if (b2.Length <= 32)
                    {
                        // Try as UTF8
                        var txt = Encoding.UTF8.GetString(b2).TrimEnd('\0');
                        var isPrintable = txt.All(c => !char.IsControl(c) || c == '\n');
                        if (isPrintable && txt.Length > 2)
                            Console.WriteLine($"    f{fnum,4}: str[{b2.Length}] = \"{txt}\"");
                        else
                            Console.WriteLine($"    f{fnum,4}: bytes[{b2.Length}] = {Convert.ToHexString(b2)}");
                    }
                    else
                        Console.WriteLine($"    f{fnum,4}: bytes[{b2.Length}] = {Convert.ToHexString(b2[..20])}...");
                }
            }
        }
    }
    catch { }
    return Task.CompletedTask;
};

await client.ConnectAsync(options);
foreach (var sn in topics)
{
    await client.SubscribeAsync(new MqttClientSubscribeOptionsBuilder()
        .WithTopicFilter($"/app/device/property/{sn}", MQTTnet.Protocol.MqttQualityOfServiceLevel.AtMostOnce).Build());
    await client.PublishAsync(new MqttApplicationMessageBuilder()
        .WithTopic($"/app/{api.UserId}/{sn}/thing/property/get")
        .WithPayload(Encoding.UTF8.GetBytes("{\"from\":\"HA\",\"id\":\"1\",\"version\":\"1.1\",\"moduleType\":0,\"operateType\":\"latestQuotas\",\"params\":{}}"))
        .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtMostOnce).Build());
}

Console.WriteLine("Listening 30s for ALL unique message types with raw fields...\n");
await Task.Delay(30000);
await client.DisconnectAsync();
Console.WriteLine($"\nSaw {seen.Count} unique message types.");
