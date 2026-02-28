using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Monovera
{
    /// <summary>
    /// SQLite-based vector store for semantic search using cosine similarity.
    /// Stores embeddings in a SQLite database with metadata.
    /// Each training overwrites existing data.
    /// Logs all operations to daily log files.
    /// </summary>
    public class VectorStore : IDisposable
    {
        private readonly string _dbPath;
        private readonly SqliteConnection _connection;
        private readonly string _logDirectory;

        public VectorStore(string dbPath)
        {
            if (string.IsNullOrWhiteSpace(dbPath))
                throw new ArgumentException("Database path cannot be empty", nameof(dbPath));

            // Change extension from .json to .db
            _dbPath = dbPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? dbPath.Replace(".json", ".db")
                : dbPath;

            // Set up log directory
            _logDirectory = Path.Combine(Directory.GetCurrentDirectory(), "log");
            Directory.CreateDirectory(_logDirectory);

            _connection = new SqliteConnection($"Data Source={_dbPath}");
            _connection.Open();

            InitializeDatabase();
            LogOperation("VectorStore initialized", $"Database: {_dbPath}");
        }

        /// <summary>
        /// Logs an operation to the daily log file.
        /// Format: YYYY-MM-DD HH:MM:SS\tLog description
        /// </summary>
        private void LogOperation(string operation, string details = "")
        {
            try
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string logFileName = $"Embedding_{DateTime.Now:yyyyMMdd}.log";
                string logFilePath = Path.Combine(_logDirectory, logFileName);

                string logEntry = string.IsNullOrWhiteSpace(details)
                    ? $"{timestamp}\t{operation}"
                    : $"{timestamp}\t{operation} - {details}";

                File.AppendAllText(logFilePath, logEntry + Environment.NewLine);
            }
            catch
            {
                // Silently fail if logging fails - don't interrupt main operation
            }
        }

        /// <summary>
        /// Creates the embeddings table if it doesn't exist.
        /// </summary>
        private void InitializeDatabase()
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS embeddings (
                    id TEXT PRIMARY KEY,
                    embedding BLOB NOT NULL,
                    metadata TEXT,
                    dimension INTEGER NOT NULL
                )";
            cmd.ExecuteNonQuery();
            LogOperation("Database initialized", "Embeddings table created/verified");
        }

        /// <summary>
        /// Clears all vectors from the database.
        /// Called at the start of training to overwrite old data.
        /// </summary>
        public void Clear()
        {
            int countBefore = Count;
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "DELETE FROM embeddings";
            cmd.ExecuteNonQuery();
            LogOperation("Database cleared", $"Deleted {countBefore} embeddings");
        }

        /// <summary>
        /// Adds or updates a vector in the database.
        /// Uses REPLACE to overwrite if exists.
        /// </summary>
        public void AddVector(string id, float[] embedding, Dictionary<string, object>? metadata = null)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("ID cannot be empty", nameof(id));

            if (embedding == null || embedding.Length == 0)
                throw new ArgumentException("Embedding cannot be empty", nameof(embedding));

            // Convert float[] to byte[] for BLOB storage
            var embeddingBytes = new byte[embedding.Length * sizeof(float)];
            Buffer.BlockCopy(embedding, 0, embeddingBytes, 0, embeddingBytes.Length);

            string metadataJson = metadata != null
                ? JsonSerializer.Serialize(metadata)
                : "{}";

            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                REPLACE INTO embeddings (id, embedding, metadata, dimension)
                VALUES (@id, @embedding, @metadata, @dimension)";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@embedding", embeddingBytes);
            cmd.Parameters.AddWithValue("@metadata", metadataJson);
            cmd.Parameters.AddWithValue("@dimension", embedding.Length);
            cmd.ExecuteNonQuery();

            LogOperation("Vector added", $"ID: {id}, Dimension: {embedding.Length}");
        }

        /// <summary>
        /// Searches for the most similar vectors using cosine similarity.
        /// Loads all vectors from DB, computes similarity, and returns top K.
        /// </summary>
        public List<VectorSearchResult> Search(float[] queryEmbedding, int topK = 10, float minSimilarity = 0.0f)
        {
            if (queryEmbedding == null || queryEmbedding.Length == 0)
                throw new ArgumentException("Query embedding cannot be empty", nameof(queryEmbedding));

            var results = new List<VectorSearchResult>();

            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT id, embedding, metadata FROM embeddings";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                string id = reader.GetString(0);
                byte[] embeddingBytes = (byte[])reader["embedding"];
                string metadataJson = reader.GetString(2);

                // Convert byte[] back to float[]
                var embedding = new float[embeddingBytes.Length / sizeof(float)];
                Buffer.BlockCopy(embeddingBytes, 0, embedding, 0, embeddingBytes.Length);

                float similarity = CosineSimilarity(queryEmbedding, embedding);

                if (similarity >= minSimilarity)
                {
                    var metadata = JsonSerializer.Deserialize<Dictionary<string, object>>(metadataJson)
                        ?? new Dictionary<string, object>();

                    results.Add(new VectorSearchResult
                    {
                        Id = id,
                        Similarity = similarity,
                        Metadata = metadata
                    });
                }
            }

            var topResults = results
                .OrderByDescending(r => r.Similarity)
                .Take(topK)
                .ToList();

            LogOperation("Search completed", $"Query dimension: {queryEmbedding.Length}, Results: {topResults.Count}/{results.Count}, TopK: {topK}, MinSimilarity: {minSimilarity:F3}");

            return topResults;
        }

        /// <summary>
        /// Gets a vector by ID from the database.
        /// </summary>
        public VectorEntry? GetVector(string id)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT id, embedding, metadata FROM embeddings WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id);

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                byte[] embeddingBytes = (byte[])reader["embedding"];
                string metadataJson = reader.GetString(2);

                var embedding = new float[embeddingBytes.Length / sizeof(float)];
                Buffer.BlockCopy(embeddingBytes, 0, embedding, 0, embeddingBytes.Length);

                var metadata = JsonSerializer.Deserialize<Dictionary<string, object>>(metadataJson)
                    ?? new Dictionary<string, object>();

                LogOperation("Vector retrieved", $"ID: {id}");

                return new VectorEntry
                {
                    Id = id,
                    Embedding = embedding,
                    Metadata = metadata
                };
            }

            LogOperation("Vector not found", $"ID: {id}");
            return null;
        }

        /// <summary>
        /// Checks if a vector exists in the database.
        /// </summary>
        public bool Contains(string id)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM embeddings WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
        }

        /// <summary>
        /// Gets the total number of vectors in the database.
        /// </summary>
        public int Count
        {
            get
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM embeddings";
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        /// <summary>
        /// Saves changes to the database (automatic with SQLite).
        /// Kept for API compatibility - SQLite auto-commits.
        /// </summary>
        public void Save()
        {
            // SQLite auto-commits, but we can ensure with a checkpoint
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
            cmd.ExecuteNonQuery();
            LogOperation("Database saved", "WAL checkpoint completed");
        }

        /// <summary>
        /// Calculates cosine similarity between two vectors.
        /// Returns a value between -1 and 1, where 1 means identical vectors.
        /// </summary>
        private static float CosineSimilarity(float[] a, float[] b)
        {
            if (a.Length != b.Length)
                throw new ArgumentException("Vectors must have the same dimension");

            float dotProduct = 0;
            float magnitudeA = 0;
            float magnitudeB = 0;

            for (int i = 0; i < a.Length; i++)
            {
                dotProduct += a[i] * b[i];
                magnitudeA += a[i] * a[i];
                magnitudeB += b[i] * b[i];
            }

            magnitudeA = (float)Math.Sqrt(magnitudeA);
            magnitudeB = (float)Math.Sqrt(magnitudeB);

            if (magnitudeA == 0 || magnitudeB == 0)
                return 0;

            return dotProduct / (magnitudeA * magnitudeB);
        }

        public void Dispose()
        {
            int finalCount = Count;
            LogOperation("VectorStore disposed", $"Final count: {finalCount} embeddings");
            _connection?.Dispose();
        }
    }

    /// <summary>
    /// Represents a vector entry in the store.
    /// </summary>
    public class VectorEntry
    {
        public string Id { get; set; } = string.Empty;
        public float[] Embedding { get; set; } = Array.Empty<float>();
        public Dictionary<string, object> Metadata { get; set; } = new();
    }

    /// <summary>
    /// Search result with similarity score.
    /// </summary>
    public class VectorSearchResult
    {
        public string Id { get; set; } = string.Empty;
        public float Similarity { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
    }
}
