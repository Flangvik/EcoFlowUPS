using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EcoFlowMonitor.Core
{
    public class MqttCredentials
    {
        public string Host { get; set; }
        public int Port { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
    }

    public class EcoFlowClient : IDisposable
    {
        private const string ApiHost = "https://api.ecoflow.com";

        private readonly HttpClient _http;
        private string _token;
        private string _userId;

        public EcoFlowClient()
        {
            _http = new HttpClient();
            _http.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
        }

        // ------------------------------------------------------------------
        // Login
        // POST /auth/login
        // Body: { email, password: Base64(password), scene: "IOT_APP", userType: "ECOFLOW" }
        // Response: data.token, data.user.userId
        // ------------------------------------------------------------------
        public async Task LoginAsync(string email, string password)
        {
            var encodedPassword = Convert.ToBase64String(Encoding.UTF8.GetBytes(password));

            var body = new JObject
            {
                ["email"]    = email,
                ["password"] = encodedPassword,
                ["scene"]    = "IOT_APP",
                ["userType"] = "ECOFLOW"
            };

            var content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
            var response = await _http.PostAsync($"{ApiHost}/auth/login", content).ConfigureAwait(false);

            var json = JObject.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Login failed ({(int)response.StatusCode}): {json["message"]}");

            var data = json["data"];
            if (data == null)
                throw new InvalidOperationException($"Login response missing 'data': {json}");

            _token  = data["token"]?.ToString()
                ?? throw new InvalidOperationException("Login response missing token");
            _userId = data["user"]?["userId"]?.ToString()
                ?? throw new InvalidOperationException("Login response missing userId");

            // Set bearer token for all subsequent requests
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _token);
        }

        // ------------------------------------------------------------------
        // Get first device (serial number + device name)
        // GET /app/user/device/list
        // Response: data[0].sn, data[0].deviceName
        // ------------------------------------------------------------------
        public async Task<(string sn, string name)> GetFirstDeviceAsync()
        {
            EnsureLoggedIn();

            var response = await _http.GetAsync($"{ApiHost}/app/user/device/list").ConfigureAwait(false);
            var json = JObject.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Device list failed ({(int)response.StatusCode}): {json["message"]}");

            var data = json["data"] as JArray;
            if (data == null || data.Count == 0)
                throw new InvalidOperationException("No devices found on this EcoFlow account");

            var first = data[0];
            var sn   = first["sn"]?.ToString()
                ?? throw new InvalidOperationException("Device list response missing 'sn'");
            var name = first["deviceName"]?.ToString() ?? sn;

            return (sn, name);
        }

        // ------------------------------------------------------------------
        // Get MQTT credentials
        // GET /iot-auth/app/certification?userId={userId}
        // Response: data.url, data.port, data.certificateAccount, data.certificatePassword
        // ------------------------------------------------------------------
        public async Task<MqttCredentials> GetMqttCredsAsync()
        {
            EnsureLoggedIn();

            var response = await _http
                .GetAsync($"{ApiHost}/iot-auth/app/certification?userId={Uri.EscapeDataString(_userId)}")
                .ConfigureAwait(false);

            var json = JObject.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"MQTT cert request failed ({(int)response.StatusCode}): {json["message"]}");

            var data = json["data"];
            if (data == null)
                throw new InvalidOperationException($"MQTT cert response missing 'data': {json}");

            var host = data["url"]?.ToString()
                ?? throw new InvalidOperationException("MQTT cert response missing 'url'");
            var portStr = data["port"]?.ToString() ?? "8883";
            if (!int.TryParse(portStr, out int port)) port = 8883;

            var username = data["certificateAccount"]?.ToString()
                ?? throw new InvalidOperationException("MQTT cert response missing 'certificateAccount'");
            var password = data["certificatePassword"]?.ToString()
                ?? throw new InvalidOperationException("MQTT cert response missing 'certificatePassword'");

            return new MqttCredentials
            {
                Host     = host,
                Port     = port,
                Username = username,
                Password = password
            };
        }

        public string UserId => _userId;

        private void EnsureLoggedIn()
        {
            if (string.IsNullOrEmpty(_token))
                throw new InvalidOperationException("Not logged in. Call LoginAsync first.");
        }

        public void Dispose()
        {
            _http?.Dispose();
        }
    }
}
