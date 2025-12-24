using Microsoft.Graph.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Globalization;
using System.Threading;

namespace Monovera
{
    public class RestAPIManager
    {
        /// <summary>
        /// Creates an HttpClient with basic authentication headers.
        /// </summary>
        /// <param name="username">The username for basic authentication.</param>
        /// <param name="password">The password for basic authentication.</param>
        /// <returns>An instance of HttpClient with the appropriate authentication headers.</returns>

        private static HttpClient CreateHttpClient(string username, string password)
        {
            var client = new HttpClient
            {
                // Prevent "hangs forever" by setting a finite timeout (adjust as needed)
                Timeout = TimeSpan.FromSeconds(30)
            };

            var byteArray = Encoding.ASCII.GetBytes($"{username}:{password}");
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));

            return client;
        }

        private static string BuildUrl(string baseUrl, string endpoint)
        {
            return $"{baseUrl?.TrimEnd('/')}/{endpoint?.TrimStart('/')}";
        }

        private static string AppendQueryString(string url, object payload)
        {
            if (payload == null) return url;

            IEnumerable<KeyValuePair<string, object?>> kvps;

            if (payload is IEnumerable<KeyValuePair<string, object?>> dict)
            {
                kvps = dict;
            }
            else
            {
                var props = payload.GetType().GetProperties()
                    .Where(p => p.GetIndexParameters().Length == 0)
                    .Select(p => new KeyValuePair<string, object?>(p.Name, p.GetValue(payload)));
                kvps = props;
            }

            var query = string.Join("&",
                kvps
                    .Where(kvp => kvp.Value != null)
                    .Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(Convert.ToString(kvp.Value, CultureInfo.InvariantCulture) ?? string.Empty)}"));

            if (string.IsNullOrWhiteSpace(query))
                return url;

            return url.Contains("?") ? $"{url}&{query}" : $"{url}?{query}";
        }

        /// <summary>
        /// Sends a GET request to the specified endpoint with optional payload as query string.
        /// </summary>
        public static async Task<string> Get(string username, string password, string baseUrl, string endpoint, object payload)
        {
            using (var client = CreateHttpClient(username, password))
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
            {
                try
                {
                    var url = BuildUrl(baseUrl, endpoint);
                    if (payload != null)
                    {
                        // Do not send a body with GET; append as querystring instead
                        url = AppendQueryString(url, payload);
                    }

                    var request = new HttpRequestMessage(new HttpMethod("GET"), url);

                    var response = await client
                        .SendAsync(request, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, cts.Token)
                        .ConfigureAwait(false);

                    response.EnsureSuccessStatusCode();

                    var responseBody = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);

                    return responseBody;
                }
                catch (HttpRequestException httpEx)
                {
                    //MessageBox.Show($"HTTP Request error: {httpEx.Message}");
                    return null;
                }
                catch (TaskCanceledException)
                {
                   // MessageBox.Show("HTTP Request timed out.");
                    return null;
                }
                catch (Exception ex)
                {
                    //MessageBox.Show($"General error: {ex.Message}");
                    return null;
                }
            }
        }

        /// <summary>
        /// Sends a PATCH request to the specified endpoint with the given payload.
        /// </summary>
        public static async Task<string> Patch(string username, string password, string baseUrl, string endpoint, object payload)
        {
            using (var client = CreateHttpClient(username, password))
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
            {
                try
                {
                    var url = BuildUrl(baseUrl, endpoint);
                    var jsonPayload = JsonConvert.SerializeObject(payload);
                    var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                    var request = new HttpRequestMessage(new HttpMethod("PATCH"), url)
                    {
                        Content = content
                    };

                    var response = await client
                        .SendAsync(request, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, cts.Token)
                        .ConfigureAwait(false);

                    response.EnsureSuccessStatusCode();

                    var responseBody = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);

                    return responseBody;
                }
                catch (HttpRequestException httpEx)
                {
                    //MessageBox.Show($"HTTP Request error: {httpEx.Message}");
                    return null;
                }
                catch (TaskCanceledException)
                {
                   // MessageBox.Show("HTTP Request timed out.");
                    return null;
                }
                catch (Exception ex)
                {
                   // MessageBox.Show($"General error: {ex.Message}");
                    return null;
                }
            }
        }

        /// <summary>
        /// Sends a POST request to the specified endpoint with the given payload.
        /// </summary>
        public static async Task<string> Post(string username, string password, string baseUrl, string endpoint, object payload)
        {
            using (var client = CreateHttpClient(username, password))
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
            {
                try
                {
                    var url = BuildUrl(baseUrl, endpoint);
                    var jsonPayload = JsonConvert.SerializeObject(payload);
                    var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                   // MessageBox.Show($"Sending POST request to {url} with payload: {jsonPayload}");

                    var request = new HttpRequestMessage(new HttpMethod("POST"), url)
                    {
                        Content = content
                    };

                    var response = await client
                        .SendAsync(request, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, cts.Token)
                        .ConfigureAwait(false);

                    response.EnsureSuccessStatusCode();

                    var responseBody = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);

                    return responseBody;
                }
                catch (HttpRequestException httpEx)
                {
                    //MessageBox.Show($"HTTP Request error: {httpEx.Message}");
                    return httpEx.Message;
                }
                catch (TaskCanceledException)
                {
                    //MessageBox.Show("HTTP Request timed out.");
                    return null;
                }
                catch (Exception ex)
                {
                    //MessageBox.Show($"General error: {ex.Message}");
                    return null;
                }
            }
        }

        /// <summary>
        /// Sends a DELETE request to the specified endpoint with optional payload.
        /// </summary>
        public static async Task<bool> Delete(string username, string password, string baseUrl, string endpoint, object payload)
        {
            using (var client = CreateHttpClient(username, password))
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
            {
                try
                {
                    var url = BuildUrl(baseUrl, endpoint);
                    MessageBox.Show($"Sending DELETE request to {url} with payload: {payload}");

                    var request = new HttpRequestMessage(new HttpMethod("DELETE"), url);

                    if (payload != null)
                    {
                        var jsonPayload = JsonConvert.SerializeObject(payload);
                        var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                        request.Content = content;
                    }

                    var response = await client
                        .SendAsync(request, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, cts.Token)
                        .ConfigureAwait(false);

                    response.EnsureSuccessStatusCode();

                    return response.IsSuccessStatusCode;
                }
                catch (HttpRequestException httpEx)
                {
                   // MessageBox.Show($"HTTP Request error: {httpEx.Message}");
                    return false;
                }
                catch (TaskCanceledException)
                {
                    //MessageBox.Show("HTTP Request timed out.");
                    return false;
                }
                catch (Exception ex)
                {
                   // MessageBox.Show($"General error: {ex.Message}");
                    return false;
                }
            }
        }
    }
}