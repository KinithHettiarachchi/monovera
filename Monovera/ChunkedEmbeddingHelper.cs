using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Monovera
{
    /// <summary>
    /// Extension to MonoveraBot for handling long texts with chunked embeddings.
    /// Splits long documents into overlapping chunks to preserve full semantic meaning.
    /// </summary>
    public static class ChunkedEmbeddingHelper
    {
        /// <summary>
        /// Splits text into chunks with overlap to preserve context at boundaries.
        /// </summary>
        public static List<TextChunk> ChunkText(string text, int chunkSize = 7000, int overlapSize = 500)
        {
            var chunks = new List<TextChunk>();
            
            if (string.IsNullOrWhiteSpace(text))
                return chunks;

            if (text.Length <= chunkSize)
            {
                chunks.Add(new TextChunk
                {
                    Text = text,
                    StartIndex = 0,
                    EndIndex = text.Length,
                    ChunkNumber = 0,
                    TotalChunks = 1
                });
                return chunks;
            }

            int chunkNumber = 0;
            int position = 0;

            while (position < text.Length)
            {
                int actualChunkSize = Math.Min(chunkSize, text.Length - position);
                string chunk = text.Substring(position, actualChunkSize);

                chunks.Add(new TextChunk
                {
                    Text = chunk,
                    StartIndex = position,
                    EndIndex = position + actualChunkSize,
                    ChunkNumber = chunkNumber,
                    TotalChunks = -1 // Will be set after all chunks created
                });

                // Move position forward, accounting for overlap
                position += actualChunkSize - overlapSize;
                
                // If remaining text is smaller than overlap, just take it all
                if (position + overlapSize >= text.Length)
                {
                    position = text.Length;
                }

                chunkNumber++;
            }

            // Set total chunks for all
            foreach (var chunk in chunks)
            {
                chunk.TotalChunks = chunks.Count;
            }

            return chunks;
        }

        /// <summary>
        /// Generates embeddings for all chunks and averages them.
        /// This preserves semantic meaning across the entire document.
        /// </summary>
        public static async Task<float[]> GetChunkedEmbeddingAsync(
            string text,
            ILlmProvider llmProvider,
            int chunkSize = 7000,
            int overlapSize = 500,
            CancellationToken cancellationToken = default)
        {
            var chunks = ChunkText(text, chunkSize, overlapSize);

            if (!chunks.Any())
            {
                throw new ArgumentException("Text is empty or resulted in no chunks", nameof(text));
            }

            // Get embedding for each chunk
            var embeddings = new List<float[]>();
            
            foreach (var chunk in chunks)
            {
                var embedding = await llmProvider.GetEmbeddingAsync(chunk.Text, cancellationToken);
                embeddings.Add(embedding);
            }

            // Average all embeddings to get a single representative vector
            return AverageEmbeddings(embeddings);
        }

        /// <summary>
        /// Averages multiple embedding vectors into a single vector.
        /// This creates a representation that captures meaning from all chunks.
        /// </summary>
        public static float[] AverageEmbeddings(List<float[]> embeddings)
        {
            if (!embeddings.Any())
                throw new ArgumentException("No embeddings to average", nameof(embeddings));

            int dimensions = embeddings[0].Length;
            var averaged = new float[dimensions];

            // Sum all vectors
            foreach (var embedding in embeddings)
            {
                if (embedding.Length != dimensions)
                    throw new ArgumentException("All embeddings must have the same dimensions");

                for (int i = 0; i < dimensions; i++)
                {
                    averaged[i] += embedding[i];
                }
            }

            // Divide by count to get average
            for (int i = 0; i < dimensions; i++)
            {
                averaged[i] /= embeddings.Count;
            }

            // Normalize the result
            return NormalizeVector(averaged);
        }

        private static float[] NormalizeVector(float[] vector)
        {
            float magnitude = (float)Math.Sqrt(vector.Sum(x => x * x));
            
            if (magnitude == 0)
                return vector;

            return vector.Select(x => x / magnitude).ToArray();
        }
    }

    /// <summary>
    /// Represents a chunk of text with metadata about its position.
    /// </summary>
    public class TextChunk
    {
        public string Text { get; set; } = string.Empty;
        public int StartIndex { get; set; }
        public int EndIndex { get; set; }
        public int ChunkNumber { get; set; }
        public int TotalChunks { get; set; }
    }
}
