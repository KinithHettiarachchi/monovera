using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Monovera
{
    /// <summary>
    /// LLM provider that uses Ollama running locally.
    /// Download Ollama from https://ollama.ai and run: ollama pull phi
    /// For embeddings: ollama pull nomic-embed-text
    /// </summary>
    public class OllamaLlmProvider : ILlmProvider, IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _modelName;
        private readonly string _embeddingModel;
        private readonly string _baseUrl;

        /// <summary>
        /// Creates a new Ollama LLM provider.
        /// </summary>
        /// <param name="modelName">Model name (e.g., "phi:latest", "gemma2:2b", "llama3.2:3b")</param>
        /// <param name="embeddingModel">Embedding model name (default: "nomic-embed-text")</param>
        /// <param name="baseUrl">Ollama API URL (default: http://localhost:11434)</param>
        public OllamaLlmProvider(
            string modelName = "phi:latest", 
            string? embeddingModel = null,
            string baseUrl = "http://localhost:11434")
        {
            _modelName = modelName ?? throw new ArgumentNullException(nameof(modelName));
            _embeddingModel = embeddingModel ?? "nomic-embed-text";
            _baseUrl = baseUrl ?? throw new ArgumentNullException(nameof(baseUrl));
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(5) // LLM responses can take time
            };
        }

        public string ProviderName => $"Ollama ({_modelName})";
        public bool SupportsEmbeddings => true;

        public async Task<bool> IsAvailableAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_baseUrl}/api/tags");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public async Task<string> GenerateResponseAsync(string prompt, CancellationToken cancellationToken = default)
        {
            try
            {
                var requestBody = new
                {
                    model = _modelName,
                    prompt = prompt,
                    stream = false,
                    options = new
                    {
                        temperature = 0.7,
                        top_p = 0.9,
                        top_k = 40
                    }
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_baseUrl}/api/generate", content, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync(cancellationToken);
                    throw new InvalidOperationException($"Ollama API error: {response.StatusCode} - {error}");
                }

                var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
                var result = JsonSerializer.Deserialize<OllamaResponse>(responseJson);

                return result?.response ?? "No response from LLM";
            }
            catch (TaskCanceledException)
            {
                return "Request timed out. The model might be loading or the prompt is too complex.";
            }
            catch (HttpRequestException ex)
            {
                return $"Connection error: {ex.Message}. Make sure Ollama is running (ollama serve).";
            }
            catch (Exception ex)
            {
                return $"Error generating response: {ex.Message}";
            }
        }

        public async Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
        {
            try
            {
                // Validate text length (nomic-embed-text has ~8192 token limit)
                if (string.IsNullOrWhiteSpace(text))
                {
                    throw new ArgumentException("Text for embedding cannot be empty", nameof(text));
                }

                var requestBody = new
                {
                    model = _embeddingModel,
                    prompt = text
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_baseUrl}/api/embeddings", content, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync(cancellationToken);

                    // Check for context length error
                    if (error.Contains("context length") || error.Contains("too long"))
                    {
                        throw new InvalidOperationException($"Text is too long for embedding model (max ~8000 chars). Text length: {text.Length} chars. Please truncate the text.");
                    }

                    throw new InvalidOperationException($"Ollama embedding error: {response.StatusCode} - {error}");
                }

                var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
                var result = JsonSerializer.Deserialize<OllamaEmbeddingResponse>(responseJson);

                if (result?.embedding == null || result.embedding.Length == 0)
                {
                    throw new InvalidOperationException("No embedding returned from Ollama");
                }

                // Normalize the embedding vector
                return NormalizeVector(result.embedding);
            }
            catch (TaskCanceledException)
            {
                throw new TimeoutException("Embedding request timed out. The embedding model might be loading.");
            }
            catch (HttpRequestException ex)
            {
                throw new InvalidOperationException($"Connection error: {ex.Message}. Make sure Ollama is running and the embedding model is installed (ollama pull {_embeddingModel}).", ex);
            }
        }

        private float[] NormalizeVector(double[] vector)
        {
            // Convert double[] to float[] and normalize
            var magnitude = Math.Sqrt(vector.Sum(x => x * x));
            return vector.Select(x => (float)(x / magnitude)).ToArray();
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }

        private class OllamaResponse
        {
            public string? model { get; set; }
            public string? response { get; set; }
            public bool done { get; set; }
        }

        private class OllamaEmbeddingResponse
        {
            public double[] embedding { get; set; } = Array.Empty<double>();
        }
    }
}
