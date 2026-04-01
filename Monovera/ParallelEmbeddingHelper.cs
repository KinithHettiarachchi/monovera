using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Monovera
{
    /// <summary>
    /// Parallel embedding generation for large datasets (10K+ issues).
    /// Uses SemaphoreSlim to control concurrency and avoid overwhelming Ollama.
    /// Reduces training time from 10 hours to 2-3 hours for 36K issues.
    /// </summary>
    public static class ParallelEmbeddingHelper
    {
        /// <summary>
        /// Generates embeddings for all issues in parallel with controlled concurrency.
        /// </summary>
        /// <param name="issues">Dictionary of issues to embed</param>
        /// <param name="llmProvider">LLM provider for embedding generation</param>
        /// <param name="vectorStore">Vector store to save embeddings</param>
        /// <param name="maxConcurrency">Maximum parallel requests (default: 5)</param>
        /// <param name="progress">Progress callback</param>
        /// <param name="cancellationToken">Cancellation token</param>
        public static async Task GenerateEmbeddingsParallelAsync(
            Dictionary<string, IssueKnowledge> issues,
            ILlmProvider llmProvider,
            VectorStore vectorStore,
            int maxConcurrency = 5,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var semaphore = new SemaphoreSlim(maxConcurrency);
            var tasks = new List<Task>();
            int completed = 0;
            int total = issues.Count;
            var startTime = DateTime.UtcNow;

            progress?.Report($"Starting parallel embedding generation (max {maxConcurrency} concurrent requests)...");

            foreach (var issue in issues.Values)
            {
                await semaphore.WaitAsync(cancellationToken);

                var task = Task.Run(async () =>
                {
                    try
                    {
                        // Create text for embedding
                        var textForEmbedding = $"{issue.Summary} {issue.Description}".Trim();
                        if (string.IsNullOrWhiteSpace(textForEmbedding))
                            textForEmbedding = issue.Key;

                        // Generate embedding (chunked if needed)
                        // Lowered threshold from 7000 to 4000 to avoid token limit errors
                        float[] embedding;
                        if (textForEmbedding.Length > 4000)
                        {
                            embedding = await ChunkedEmbeddingHelper.GetChunkedEmbeddingAsync(
                                textForEmbedding,
                                llmProvider,
                                chunkSize: 4000,
                                overlapSize: 500,
                                cancellationToken);
                        }
                        else
                        {
                            try
                            {
                                embedding = await llmProvider.GetEmbeddingAsync(textForEmbedding, cancellationToken);
                            }
                            catch (Exception ex) when (ex.Message.Contains("too long", StringComparison.OrdinalIgnoreCase))
                            {
                                // Fallback: If even "short" text is too long, use chunking
                                progress?.Report($"  ⚠️ Text length issue for {issue.Key}, using chunking fallback...");
                                embedding = await ChunkedEmbeddingHelper.GetChunkedEmbeddingAsync(
                                    textForEmbedding,
                                    llmProvider,
                                    chunkSize: 3000,
                                    overlapSize: 500,
                                    cancellationToken);
                            }
                        }

                        issue.Embedding = embedding;

                        // Store in vector database
                        vectorStore.AddVector(issue.Key, embedding, new Dictionary<string, object>
                        {
                            ["Summary"] = issue.Summary,
                            ["IssueType"] = issue.IssueType,
                            ["ProjectName"] = issue.ProjectName,
                            ["TextLength"] = textForEmbedding.Length
                        });

                        // Progress reporting
                        var currentCompleted = Interlocked.Increment(ref completed);
                        if (currentCompleted % 50 == 0 || currentCompleted == total)
                        {
                            var elapsed = DateTime.UtcNow - startTime;
                            var estimatedTotal = elapsed.TotalSeconds / currentCompleted * total;
                            var remaining = TimeSpan.FromSeconds(Math.Max(0, estimatedTotal - elapsed.TotalSeconds));

                            progress?.Report($"Generated {currentCompleted}/{total} embeddings " +
                                           $"({currentCompleted * 100.0 / total:F1}%) " +
                                           $"Elapsed: {elapsed:hh\\:mm\\:ss} " +
                                           $"Remaining: ~{remaining:hh\\:mm\\:ss}");
                        }
                    }
                    catch (Exception ex)
                    {
                        progress?.Report($"Warning: Failed to generate embedding for {issue.Key}: {ex.Message}");
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, cancellationToken);

                tasks.Add(task);
            }

            // Wait for all tasks to complete
            await Task.WhenAll(tasks);

            var totalElapsed = DateTime.UtcNow - startTime;
            progress?.Report($"✅ Completed {completed}/{total} embeddings in {totalElapsed:hh\\:mm\\:ss}");
        }

        /// <summary>
        /// Estimates training time based on sample performance.
        /// </summary>
        public static async Task<TimeSpan> EstimateTrainingTimeAsync(
            ILlmProvider llmProvider,
            int totalIssues,
            int maxConcurrency = 5)
        {
            // Generate 5 sample embeddings to estimate speed
            var samples = new[] { "Sample 1", "Sample 2", "Sample 3", "Sample 4", "Sample 5" };
            var startTime = DateTime.UtcNow;

            var tasks = samples.Select(s => llmProvider.GetEmbeddingAsync(s)).ToArray();
            await Task.WhenAll(tasks);

            var elapsed = DateTime.UtcNow - startTime;
            var avgTimePerEmbedding = elapsed.TotalSeconds / samples.Length;

            // Estimate with parallelization factor
            var estimatedSeconds = (totalIssues * avgTimePerEmbedding) / maxConcurrency;
            return TimeSpan.FromSeconds(estimatedSeconds);
        }
    }
}
