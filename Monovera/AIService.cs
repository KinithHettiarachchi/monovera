using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Monovera
{
    public static class AIService
    {
        // Simple helper used by frmTalkToAI.LoadAIContext (non-RAG)
        // Sends the prompt to local Ollama and returns the response text.
        public static async Task<string> AskAI(string prompt, string promptType = "")
        {
            try
            {
                using var http = new HttpClient { BaseAddress = new Uri("http://localhost:11434") };
                var payload = new
                {
                    model = "llama3.1:8b",
                    prompt = prompt,
                    stream = false
                };

                var json = JsonConvert.SerializeObject(payload);
                using var req = new StringContent(json, Encoding.UTF8, "application/json");
                using var res = await http.PostAsync("/api/generate", req);
                res.EnsureSuccessStatusCode();

                var body = await res.Content.ReadAsStringAsync();
                var obj = JObject.Parse(body);
                return obj.Value<string>("response") ?? string.Empty;
            }
            catch (Exception ex)
            {
                return "Error: " + ex.Message;
            }
        }
    }
}