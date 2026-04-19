using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EcoFlowMonitor.Client;

public class EcoFlowClient : IDisposable
{
    private const string ApiHost = "https://api.ecoflow.com";

    private readonly HttpClient _http;
    private string? _token;
    private string? _userId;

    public EcoFlowClient()
    {
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.Add("lang", "en_US");
    }

    // ------------------------------------------------------------------
    // Login
    // POST /auth/login
    // ------------------------------------------------------------------
    public async Task LoginAsync(string email, string password)
    {
        var body = new JsonObject
        {
            ["email"]    = email,
            ["password"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(password)),
            ["scene"]    = "IOT_APP",
            ["userType"] = "ECOFLOW"
        };

        var content  = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        var response = await _http.PostAsync($"{ApiHost}/auth/login", content).ConfigureAwait(false);
        var json     = JsonNode.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Login failed ({(int)response.StatusCode}): {json?["message"]}");

        var data = json?["data"] ?? throw new InvalidOperationException($"Login response missing 'data': {json}");

        _token  = data["token"]?.GetValue<string>()           ?? throw new InvalidOperationException("Login response missing token");
        _userId = data["user"]?["userId"]?.GetValue<string>() ?? throw new InvalidOperationException("Login response missing userId");

        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
    }

    // ------------------------------------------------------------------
    // Get all devices on the account
    // GET /app/user/device/list
    // ------------------------------------------------------------------
    public async Task<List<(string sn, string name)>> GetAllDevicesAsync()
    {
        EnsureLoggedIn();

        var response = await _http.GetAsync($"{ApiHost}/app/user/device/list").ConfigureAwait(false);
        var json     = JsonNode.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Device list failed ({(int)response.StatusCode}): {json?["message"]}");

        var result = new List<(string sn, string name)>();
        if (json?["data"] is JsonArray data)
            foreach (var item in data)
            {
                var sn = item?["sn"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(sn))
                    result.Add((sn, item?["deviceName"]?.GetValue<string>() ?? sn));
            }
        return result;
    }

    // ------------------------------------------------------------------
    // Get MQTT credentials
    // GET /iot-auth/app/certification?userId={userId}
    // ------------------------------------------------------------------
    public async Task<MqttCredentials> GetMqttCredsAsync()
    {
        EnsureLoggedIn();

        var response = await _http
            .GetAsync($"{ApiHost}/iot-auth/app/certification?userId={Uri.EscapeDataString(_userId!)}")
            .ConfigureAwait(false);

        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"MQTT cert request failed ({(int)response.StatusCode}): {json?["message"]}");

        var data     = json?["data"] ?? throw new InvalidOperationException($"MQTT cert response missing 'data': {json}");
        var portStr  = data["port"]?.ToString() ?? "8883";
        if (!int.TryParse(portStr, out int port)) port = 8883;

        return new MqttCredentials(
            Host:     data["url"]?.GetValue<string>()                 ?? throw new InvalidOperationException("MQTT cert missing 'url'"),
            Port:     port,
            Username: data["certificateAccount"]?.GetValue<string>()  ?? throw new InvalidOperationException("MQTT cert missing 'certificateAccount'"),
            Password: data["certificatePassword"]?.GetValue<string>() ?? throw new InvalidOperationException("MQTT cert missing 'certificatePassword'")
        );
    }

    public string? UserId => _userId;

    private void EnsureLoggedIn()
    {
        if (string.IsNullOrEmpty(_token))
            throw new InvalidOperationException("Not logged in. Call LoginAsync first.");
    }

    public void Dispose() => _http?.Dispose();
}
