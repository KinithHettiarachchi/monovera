using System.Threading;
using System.Threading.Tasks;

namespace Monovera
{
    /// <summary>
    /// Interface for Large Language Model providers that generate intelligent responses.
    /// </summary>
    public interface ILlmProvider
    {
        /// <summary>
        /// Generates a response based on a prompt using the AI model.
        /// </summary>
        /// <param name="prompt">The prompt to send to the AI</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>AI-generated response</returns>
        Task<string> GenerateResponseAsync(string prompt, CancellationToken cancellationToken = default);

        /// <summary>
        /// Generates a vector embedding for the given text.
        /// Used for semantic search and similarity matching.
        /// </summary>
        /// <param name="text">Text to convert to embedding vector</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Embedding vector (normalized)</returns>
        Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks if the LLM provider is available and ready.
        /// </summary>
        Task<bool> IsAvailableAsync();

        /// <summary>
        /// Gets the name of the LLM provider.
        /// </summary>
        string ProviderName { get; }

        /// <summary>
        /// Gets whether this provider supports embeddings.
        /// </summary>
        bool SupportsEmbeddings { get; }
    }
}
