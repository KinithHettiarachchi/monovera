using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Monovera
{
    // Public facade for convenient use from the rest of the solution.
    public static class OllamaRAG
    {
        public static Task<int> RunAsync(string[] args) => Cli.RunAsync(args);

        public static async Task BuildIndexAsync(string dbPath, int chunkSize = 1500, int hop = 2, string? ollamaBaseUrl = null)
        {
            var db = new Db(dbPath);
            var repo = new IssueRepo(db);
            var issues = repo.LoadAll();
            var graph = new Graph(issues);
            var ollama = new OllamaClient(ollamaBaseUrl);
            var emb = new EmbeddingService(ollama);

            var builder = new IndexBuilder(repo, graph, emb);
            await builder.BuildAsync(issues, chunkSize, hop);
        }

        // Overload with progress and cancellation support (used by frmTalkToAI)
        public static async Task BuildIndexAsync(
            string dbPath,
            int chunkSize,
            int hop,
            string? ollamaBaseUrl,
            IProgress<string>? progress,
            CancellationToken cancellationToken)
        {
            var db = new Db(dbPath);
            var repo = new IssueRepo(db);
            var issues = repo.LoadAll();
            var graph = new Graph(issues);
            var ollama = new OllamaClient(ollamaBaseUrl);
            var emb = new EmbeddingService(ollama);

            var builder = new IndexBuilder(repo, graph, emb);
            await builder.BuildAsync(issues, chunkSize, hop, progress, cancellationToken);
        }

        public static async Task<string> AskAsync(string dbPath, string question, int topk = 12, string? seedKey = null, string? systemStyle = null, string? ollamaBaseUrl = null)
        {
            var db = new Db(dbPath);
            var repo = new IssueRepo(db);
            repo.EnsureEmbeddingTable();
            var ollama = new OllamaClient(ollamaBaseUrl);
            var emb = new EmbeddingService(ollama);
            var retr = new Retriever(repo, emb);
            var top = await retr.SearchAsync(question, topk, seedKey);
            var prom = new Prompter(ollama);
            return await prom.GenerateAsync(question, top, systemStyle);
        }

        // Streaming variant delivering deltas (used by frmTalkToAI)
        public static async Task AskStreamAsync(
            string dbPath,
            string question,
            Action<string> onDelta,
            int topk = 12,
            string? seedKey = null,
            string? systemStyle = null,
            string? ollamaBaseUrl = null,
            CancellationToken cancellationToken = default)
        {
            var db = new Db(dbPath);
            var repo = new IssueRepo(db);
            repo.EnsureEmbeddingTable();
            var ollama = new OllamaClient(ollamaBaseUrl);
            var emb = new EmbeddingService(ollama);
            var retr = new Retriever(repo, emb);
            var top = await retr.SearchAsync(question, topk, seedKey);
            var prom = new Prompter(ollama);
            await prom.GenerateStreamAsync(question, top, systemStyle, onDelta, cancellationToken);
        }

        public static async Task<string> SummaryAsync(string dbPath, string key, string? ollamaBaseUrl = null)
        {
            var db = new Db(dbPath);
            var repo = new IssueRepo(db);
            repo.EnsureEmbeddingTable();
            var issues = repo.LoadAll();
            if (!issues.TryGetValue(key, out var issue))
                throw new InvalidOperationException($"Issue not found: {key}");

            var allChunks = repo.LoadAllChunks();
            var prom = new Prompter(new OllamaClient(ollamaBaseUrl));
            return await prom.SummaryForKeyAsync(issue, allChunks);
        }

        public static async Task<string> TestsAsync(string dbPath, string key, string? ollamaBaseUrl = null)
        {
            var db = new Db(dbPath);
            var repo = new IssueRepo(db);
            repo.EnsureEmbeddingTable();
            var issues = repo.LoadAll();
            if (!issues.TryGetValue(key, out var issue))
                throw new InvalidOperationException($"Issue not found: {key}");

            var allChunks = repo.LoadAllChunks();
            var prom = new Prompter(new OllamaClient(ollamaBaseUrl));
            return await prom.TestCasesForKeyAsync(issue, allChunks);
        }

        public static async Task<string> GuideAsync(string dbPath, string key, string? ollamaBaseUrl = null)
        {
            var db = new Db(dbPath);
            var repo = new IssueRepo(db);
            repo.EnsureEmbeddingTable();
            var issues = repo.LoadAll();
            if (!issues.TryGetValue(key, out var issue))
                throw new InvalidOperationException($"Issue not found: {key}");

            var allChunks = repo.LoadAllChunks();
            var prom = new Prompter(new OllamaClient(ollamaBaseUrl));
            return await prom.UserGuideForKeyAsync(issue, allChunks);
        }
    }

    // Jira + Ollama: Complete Local AI (RAG + optional finetune) – Single-file C# Console App
    // .NET 8+ recommended.
    //
    // Prereqs:
    // 1) Ollama installed and running (http://localhost:11434)
    // 2) Pull models once:
    //      ollama pull llama3.1:8b
    //      ollama pull nomic-embed-text
    // 3) Your SQLite file: monovera.sqlite with table `issue` as described.
    //
    // Commands:
    //   index   --db monovera.sqlite [--chunksize 1500] [--hop 2]
    //   ask     --db monovera.sqlite --q "How to..." [--topk 12] [--sys "style"] [--seed DEV-123]
    //   summary --db monovera.sqlite --key DEV-123
    //   tests   --db monovera.sqlite --key DEV-123
    //   guide   --db monovera.sqlite --key DEV-123
    //   export-jsonl --db monovera.sqlite --out jira_training.jsonl
    //   write-modelfile --out Modelfile
    //   finetune --name jira-bot --modelfile Modelfile
    //
    // Storage:
    //   Creates table issue_chunk_embeddings(issue_key, chunk_index, text, vector BLOB, meta JSON) in the same DB.

    class IssueRecord
    {
        public string Key { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ParentKey { get; set; } = string.Empty;
        public List<string> Children { get; set; } = new();
        public List<string> Relates { get; set; } = new();
        public string IssueType { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public string ProjectCode { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string History { get; set; } = string.Empty;
        public string Attachments { get; set; } = string.Empty;
    }

    class ChunkEmbedding
    {
        public string IssueKey { get; set; } = string.Empty;
        public int ChunkIndex { get; set; }
        public string Text { get; set; } = string.Empty;
        public float[] Vector { get; set; } = Array.Empty<float>();
        public JObject Meta { get; set; } = new JObject();
    }

    class Db
    {
        private readonly string _path;
        public Db(string path) { _path = path; }
        public SqliteConnection Open() { var c = new SQLiteConnection($"Data Source={_path}"); c.Open(); return c; }
    }

    internal class SQLiteConnection : SqliteConnection
    {
        public SQLiteConnection(string? connectionString) : base(connectionString)
        {
        }
    }

    class IssueRepo
    {
        private readonly Db _db;
        public IssueRepo(Db db) { _db = db; }

        public void EnsureEmbeddingTable()
        {
            using var conn = _db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"CREATE TABLE IF NOT EXISTS issue_chunk_embeddings (
                issue_key   TEXT NOT NULL,
                chunk_index INTEGER NOT NULL,
                text        TEXT NOT NULL,
                vector      BLOB NOT NULL,
                meta        TEXT NOT NULL,
                PRIMARY KEY(issue_key, chunk_index)
            );";
            cmd.ExecuteNonQuery();
        }

        public Dictionary<string, IssueRecord> LoadAll()
        {
            var dict = new Dictionary<string, IssueRecord>(StringComparer.OrdinalIgnoreCase);
            using var conn = _db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT KEY, SUMMARY, DESCRIPTION, PARENTKEY, CHILDRENKEYS, RELATESKEYS, ISSUETYPE, PROJECTNAME, PROJECTCODE, STATUS, HISTORY, ATTACHMENTS FROM issue";
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                string s(string col) => rd[col] == DBNull.Value ? string.Empty : Convert.ToString(rd[col]) ?? string.Empty;
                var rec = new IssueRecord
                {
                    Key = s("KEY"),
                    Summary = s("SUMMARY"),
                    Description = s("DESCRIPTION"),
                    ParentKey = s("PARENTKEY"),
                    Children = SplitKeys(s("CHILDRENKEYS")),
                    Relates = SplitKeys(s("RELATESKEYS")),
                    IssueType = s("ISSUETYPE"),
                    ProjectName = s("PROJECTNAME"),
                    ProjectCode = s("PROJECTCODE"),
                    Status = s("STATUS"),
                    History = s("HISTORY"),
                    Attachments = s("ATTACHMENTS")
                };
                if (!string.IsNullOrWhiteSpace(rec.Key))
                    dict[rec.Key] = rec;
            }
            return dict;
        }

        static List<string> SplitKeys(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return new();
            return s.Split(new[] { ',', ';', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        public void UpsertChunk(ChunkEmbedding ce)
        {
            using var conn = _db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"INSERT INTO issue_chunk_embeddings(issue_key,chunk_index,text,vector,meta)
                                VALUES(@k,@i,@t,@v,@m)
                                ON CONFLICT(issue_key,chunk_index) DO UPDATE SET text=excluded.text, vector=excluded.vector, meta=excluded.meta";
            cmd.Parameters.AddWithValue("@k", ce.IssueKey);
            cmd.Parameters.AddWithValue("@i", ce.ChunkIndex);
            cmd.Parameters.AddWithValue("@t", ce.Text);
            var param = cmd.CreateParameter();
            param.ParameterName = "@v";
            param.DbType = DbType.Binary;
            param.Value = FloatArrayToBytes(ce.Vector);
            cmd.Parameters.Add(param);
            cmd.Parameters.AddWithValue("@m", ce.Meta.ToString(Formatting.None));
            cmd.ExecuteNonQuery();
        }

        public List<ChunkEmbedding> LoadAllChunks()
        {
            using var conn = _db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT issue_key, chunk_index, text, vector, meta FROM issue_chunk_embeddings";
            using var rd = cmd.ExecuteReader();
            var list = new List<ChunkEmbedding>();
            while (rd.Read())
            {
                var bytes = rd["vector"] as byte[] ?? Array.Empty<byte>();
                var metaStr = Convert.ToString(rd["meta"]);
                var ce = new ChunkEmbedding
                {
                    IssueKey = Convert.ToString(rd["issue_key"]) ?? string.Empty,
                    ChunkIndex = Convert.ToInt32(rd["chunk_index"]),
                    Text = Convert.ToString(rd["text"]) ?? string.Empty,
                    Vector = BytesToFloatArray(bytes),
                    Meta = string.IsNullOrWhiteSpace(metaStr) ? new JObject() : JObject.Parse(metaStr!)
                };
                list.Add(ce);
            }
            return list;
        }

        static byte[] FloatArrayToBytes(float[] arr)
        {
            var bytes = new byte[arr.Length * sizeof(float)];
            Buffer.BlockCopy(arr, 0, bytes, 0, bytes.Length);
            return bytes;
        }

        static float[] BytesToFloatArray(byte[] bytes)
        {
            if (bytes.Length == 0) return Array.Empty<float>();
            var arr = new float[bytes.Length / sizeof(float)];
            Buffer.BlockCopy(bytes, 0, arr, 0, bytes.Length);
            return arr;
        }
    }

    class Graph
    {
        private readonly Dictionary<string, IssueRecord> _issues;
        public Graph(Dictionary<string, IssueRecord> issues) { _issues = issues; }

        public IEnumerable<string> Neighborhood(string key, int hop)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var q = new Queue<(string k, int d)>();
            void Enq(string k, int d) { if (!string.IsNullOrWhiteSpace(k) && seen.Add(k)) q.Enqueue((k, d)); }
            Enq(key, 0);
            while (q.Count > 0)
            {
                var (k, d) = q.Dequeue();
                yield return k;
                if (d >= hop) continue;
                if (_issues.TryGetValue(k, out var r))
                {
                    if (!string.IsNullOrWhiteSpace(r.ParentKey)) Enq(r.ParentKey, d + 1);
                    foreach (var c in r.Children) Enq(c, d + 1);
                    foreach (var rel in r.Relates) Enq(rel, d + 1);
                }
            }
        }

        public string PathToRoot(string key)
        {
            var parts = new List<string>();
            var cur = key;
            int safety = 0;
            while (!string.IsNullOrWhiteSpace(cur) && safety++ < 50)
            {
                parts.Add(cur);
                if (!_issues.TryGetValue(cur, out var r) || string.IsNullOrWhiteSpace(r.ParentKey)) break;
                cur = r.ParentKey;
            }
            parts.Reverse();
            return string.Join(" > ", parts);
        }
    }

    class EmbeddingService
    {
        private readonly OllamaClient _ollama;
        public EmbeddingService(OllamaClient ollama) { _ollama = ollama; }

        public async Task<float[]> EmbedAsync(string text)
        {
            var payload = new { model = "nomic-embed-text", input = text };
            var resp = await _ollama.PostAsync("/api/embeddings", payload);

            // Handle either { "embedding": [..] } or { "embeddings": [[..]] }
            static float[] ToFloats(JArray arr) => arr.Select(x => (float)(x!.Value<double>())).ToArray();

            if (resp["embedding"] is JArray single)
                return ToFloats(single);

            if (resp["embeddings"] is JArray many && many.First is JArray first)
                return ToFloats(first);

            throw new InvalidOperationException("Unexpected embeddings response from Ollama.");
        }
    }

    class IndexBuilder
    {
        private readonly IssueRepo _repo;
        private readonly Graph _graph;
        private readonly EmbeddingService _emb;
        public IndexBuilder(IssueRepo repo, Graph graph, EmbeddingService emb) { _repo = repo; _graph = graph; _emb = emb; }

        public async Task BuildAsync(Dictionary<string, IssueRecord> issues, int chunkSize = 1500, int hop = 2)
        {
            await BuildAsync(issues, chunkSize, hop, progress: null, cancellationToken: CancellationToken.None);
        }

        public async Task BuildAsync(
            Dictionary<string, IssueRecord> issues,
            int chunkSize,
            int hop,
            IProgress<string>? progress,
            CancellationToken cancellationToken)
        {
            _repo.EnsureEmbeddingTable();
            var total = issues.Count;
            int cur = 0;

            foreach (var kv in issues)
            {
                cancellationToken.ThrowIfCancellationRequested();
                cur++;
                var r = kv.Value;
                progress?.Report($"[{cur}/{total}] Preparing {r.Key}");
                var ctx = BuildRichContext(r, issues, _graph, hop);
                var chunks = Chunk(ctx, chunkSize);
                int idx = 0;
                foreach (var ch in chunks)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    progress?.Report($"[{cur}/{total}] Embedding {r.Key} (chunk {idx + 1}/{chunks.Count})");
                    var vec = await _emb.EmbedAsync(ch);
                    var meta = new JObject
                    {
                        ["summary"] = r.Summary,
                        ["issuetype"] = r.IssueType,
                        ["status"] = r.Status,
                        ["project"] = r.ProjectCode,
                        ["path"] = _graph.PathToRoot(r.Key)
                    };
                    var ce = new ChunkEmbedding { IssueKey = r.Key, ChunkIndex = idx++, Text = ch, Vector = vec, Meta = meta };
                    _repo.UpsertChunk(ce);
                }
            }
        }

        static string BuildRichContext(IssueRecord r, Dictionary<string, IssueRecord> all, Graph g, int hop)
        {
            var lines = new List<string>();
            lines.Add($"KEY: {r.Key}");
            if (!string.IsNullOrWhiteSpace(r.ProjectCode)) lines.Add($"PROJECT: {r.ProjectCode} – {r.ProjectName}");
            if (!string.IsNullOrWhiteSpace(r.IssueType)) lines.Add($"TYPE: {r.IssueType}");
            if (!string.IsNullOrWhiteSpace(r.Status)) lines.Add($"STATUS: {r.Status}");
            var path = g.PathToRoot(r.Key);
            if (!string.IsNullOrWhiteSpace(path)) lines.Add($"PATH: {path}");
            lines.Add($"SUMMARY: {r.Summary}");
            if (!string.IsNullOrWhiteSpace(r.Description)) { lines.Add("DESCRIPTION:"); lines.Add(r.Description); }
            if (!string.IsNullOrWhiteSpace(r.History)) { lines.Add("HISTORY:"); lines.Add(r.History); }
            if (!string.IsNullOrWhiteSpace(r.Attachments)) { lines.Add("ATTACHMENTS (names/ids):"); lines.Add(r.Attachments); }

            var rels = g.Neighborhood(r.Key, hop).Where(k => !k.Equals(r.Key, StringComparison.OrdinalIgnoreCase)).Take(50).ToList();
            if (rels.Count > 0)
            {
                lines.Add("NEIGHBORHOOD:");
                foreach (var k in rels)
                {
                    if (all.TryGetValue(k, out var rr))
                    {
                        lines.Add($"- {k}: {rr.Summary}");
                    }
                }
            }
            return string.Join("\n", lines);
        }

        static List<string> Chunk(string text, int maxChars)
        {
            var chunks = new List<string>();
            if (string.IsNullOrEmpty(text)) return chunks;
            for (int i = 0; i < text.Length; i += maxChars)
                chunks.Add(text.Substring(i, Math.Min(maxChars, text.Length - i)));
            return chunks;
        }
    }

    class Retriever
    {
        private readonly IssueRepo _repo;
        private readonly EmbeddingService _emb;
        public Retriever(IssueRepo repo, EmbeddingService emb) { _repo = repo; _emb = emb; }

        static double Cosine(float[] a, float[] b)
        {
            if (a.Length == 0 || b.Length == 0) return 0.0;
            double dot = 0, na = 0, nb = 0;
            int n = Math.Min(a.Length, b.Length);
            for (int i = 0; i < n; i++) { dot += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
            return dot / (Math.Sqrt(na) * Math.Sqrt(nb) + 1e-9);
        }

        public async Task<List<ChunkEmbedding>> SearchAsync(string query, int topk = 12, string? seedKey = null)
        {
            var qvec = await _emb.EmbedAsync(query);
            var all = _repo.LoadAllChunks();

            var boost = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(seedKey))
            {
                boost.Add(seedKey);
            }

            var scored = all.Select(ch => new
            {
                ch,
                score = Cosine(qvec, ch.Vector) + (boost.Contains(ch.IssueKey) ? 0.05 : 0.0)
            })
            .OrderByDescending(x => x.score)
            .Take(topk)
            .Select(x => x.ch)
            .ToList();

            return scored;
        }
    }

    class Prompter
    {
        private readonly OllamaClient _ollama;
        public Prompter(OllamaClient o) { _ollama = o; }

        private static string BuildContextBlock(IEnumerable<ChunkEmbedding> chunks)
        {
            var sb = new StringBuilder();
            int i = 1;
            foreach (var c in chunks)
            {
                sb.AppendLine($"### Doc {i++} — {c.IssueKey} (chunk {c.ChunkIndex})");
                if (c.Meta.TryGetValue("path", out var path)) sb.AppendLine($"PATH: {path}");
                if (c.Meta.TryGetValue("summary", out var sum)) sb.AppendLine($"SUMMARY: {sum}");
                sb.AppendLine(c.Text);
                sb.AppendLine();
            }
            return sb.ToString();
        }

        public async Task<string> GenerateAsync(string instruction, IEnumerable<ChunkEmbedding> chunks, string? systemStyle = null)
        {
            var context = BuildContextBlock(chunks);
            var sys = string.IsNullOrWhiteSpace(systemStyle) ? DefaultSystem() : systemStyle + "\n" + DefaultSystem();
            var prompt = $@"You are an expert Jira requirements engineer and technical writer.
Follow the SYSTEM rules below, use only the provided CONTEXT.

SYSTEM:
{sys}

CONTEXT:
{context}

USER TASK:
{instruction}

Answer:";
            var resp = await _ollama.PostAsync("/api/generate", new { model = "llama3.1:8b", prompt = prompt, stream = false });
            return resp.Value<string>("response") ?? string.Empty;
        }

        // Streaming generation for live UI
        public async Task GenerateStreamAsync(
            string instruction,
            IEnumerable<ChunkEmbedding> chunks,
            string? systemStyle,
            Action<string> onDelta,
            CancellationToken cancellationToken)
        {
            var context = BuildContextBlock(chunks);
            var sys = string.IsNullOrWhiteSpace(systemStyle) ? DefaultSystem() : systemStyle + "\n" + DefaultSystem();
            var prompt = $@"You are an expert Jira requirements engineer and technical writer.
Follow the SYSTEM rules below, use only the provided CONTEXT.

SYSTEM:
{sys}

CONTEXT:
{context}

USER TASK:
{instruction}

Answer:";
            await _ollama.GenerateStreamAsync("llama3.1:8b", prompt, onDelta, cancellationToken);
        }

        static string DefaultSystem()
        {
            return @"- If the answer is not in context, say you don't have enough info.
- Always cite issue keys inline like [DEV-123].
- Prefer structured outputs: bullet points, tables, numbered steps.
- Keep references to parent/child/related issues when summarizing.
- For test cases: use Gherkin-style scenarios when appropriate.
- For user guides: produce step-by-step instructions and prerequisites.";
        }

        public async Task<string> SummaryForKeyAsync(IssueRecord issue, IEnumerable<ChunkEmbedding> allChunks)
        {
            var ctx = allChunks.Where(c => c.IssueKey.Equals(issue.Key, StringComparison.OrdinalIgnoreCase)).Take(8).ToList();
            var instr = $"Summarize requirement [{issue.Key}] with purpose, scope, key behaviors, constraints, dependencies, and acceptance criteria. Include referenced children and related issues.";
            return await GenerateAsync(instr, ctx);
        }

        public async Task<string> TestCasesForKeyAsync(IssueRecord issue, IEnumerable<ChunkEmbedding> allChunks)
        {
            var ctx = allChunks.Where(c => c.IssueKey.Equals(issue.Key, StringComparison.OrdinalIgnoreCase)).Take(8).ToList();
            var instr = $"Generate comprehensive test cases for [{issue.Key}] using Gherkin (Feature/Scenario/Given-When-Then) and a coverage checklist. Include edge cases and reference children/related keys.";
            return await GenerateAsync(instr, ctx);
        }

        public async Task<string> UserGuideForKeyAsync(IssueRecord issue, IEnumerable<ChunkEmbedding> allChunks)
        {
            var ctx = allChunks.Where(c => c.IssueKey.Equals(issue.Key, StringComparison.OrdinalIgnoreCase)).Take(8).ToList();
            var instr = $"Write a user guide for [{issue.Key}] including Overview, Prerequisites, Step-by-step usage, Notes, and Troubleshooting. Reference related and child issues where relevant.";
            return await GenerateAsync(instr, ctx);
        }
    }

    class OllamaClient
    {
        private readonly HttpClient _http;
        private readonly string _base;
        public OllamaClient(string? baseUrl = null)
        {
            _base = string.IsNullOrWhiteSpace(baseUrl) ? "http://localhost:11434" : baseUrl!;
            _http = new HttpClient { BaseAddress = new Uri(_base) };
        }

        public async Task<JObject> PostAsync(string path, object payload)
        {
            var json = JsonConvert.SerializeObject(payload);
            using var req = new StringContent(json, Encoding.UTF8, "application/json");
            var res = await _http.PostAsync(path, req);
            res.EnsureSuccessStatusCode();
            var s = await res.Content.ReadAsStringAsync();
            return JObject.Parse(s);
        }

        // Streaming wrapper for /api/generate with stream=true (JSONL)
        public async Task GenerateStreamAsync(
    string model,
    string prompt,
    Action<string> onDelta,
    CancellationToken cancellationToken)
        {
            var payload = new
            {
                model = model,
                prompt = prompt,
                stream = true
            };

            var json = JsonConvert.SerializeObject(payload);
            using var httpReq = new HttpRequestMessage(HttpMethod.Post, "/api/generate")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            using var res = await _http.SendAsync(httpReq, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            res.EnsureSuccessStatusCode();

            using var stream = await res.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line)) continue;

                JObject obj;
                try { obj = JObject.Parse(line); }
                catch { continue; }

                var delta = obj.Value<string>("response");
                if (!string.IsNullOrEmpty(delta))
                    onDelta(delta);

                var done = obj.Value<bool?>("done") ?? false;
                if (done) break;
            }
        }
    }

    class Cli
    {
        public static async Task<int> RunAsync(string[] args)
        {
            if (args.Length == 0) { PrintHelp(); return 1; }
            string cmd = args[0].ToLowerInvariant();
            var opts = ParseArgs(args.Skip(1).ToArray());
            string dbPath = opts.GetValueOrDefault("--db", "monovera.sqlite");

            var db = new Db(dbPath);
            var repo = new IssueRepo(db);
            var issues = repo.LoadAll();
            var graph = new Graph(issues);
            var ollama = new OllamaClient(opts.GetValueOrDefault("--ollama", null));
            var emb = new EmbeddingService(ollama);

            switch (cmd)
            {
                case "index":
                    {
                        int chunk = int.Parse(opts.GetValueOrDefault("--chunksize", "1500"));
                        int hop = int.Parse(opts.GetValueOrDefault("--hop", "2"));
                        var builder = new IndexBuilder(repo, graph, emb);
                        await builder.BuildAsync(issues, chunk, hop);
                        Console.WriteLine("Index built.");
                        return 0;
                    }
                case "ask":
                    {
                        if (!opts.TryGetValue("--q", out var q) || string.IsNullOrWhiteSpace(q)) { Console.WriteLine("--q required"); return 2; }
                        repo.EnsureEmbeddingTable();
                        int topk = int.Parse(opts.GetValueOrDefault("--topk", "12"));
                        string? seed = opts.GetValueOrDefault("--seed", null);
                        var retr = new Retriever(repo, emb);
                        var top = await retr.SearchAsync(q!, topk, seed);
                        if (top.Count == 0)
                        {
                            Console.WriteLine("No embeddings found. Run: index --db <path> first.");
                            return 4;
                        }
                        var prom = new Prompter(ollama);
                        var sys = opts.GetValueOrDefault("--sys", null);
                        var answer = await prom.GenerateAsync(q!, top, sys);
                        Console.WriteLine(answer);
                        return 0;
                    }
                case "summary":
                case "tests":
                case "guide":
                    {
                        if (!opts.TryGetValue("--key", out var key) || string.IsNullOrWhiteSpace(key)) { Console.WriteLine("--key required"); return 2; }
                        repo.EnsureEmbeddingTable();
                        var allChunks = repo.LoadAllChunks();
                        if (!issues.TryGetValue(key, out var issue)) { Console.WriteLine($"Issue not found: {key}"); return 3; }
                        var prom = new Prompter(ollama);
                        string result = cmd switch
                        {
                            "summary" => await prom.SummaryForKeyAsync(issue, allChunks),
                            "tests" => await prom.TestCasesForKeyAsync(issue, allChunks),
                            _ => await prom.UserGuideForKeyAsync(issue, allChunks)
                        };
                        Console.WriteLine(result);
                        return 0;
                    }
                case "export-jsonl":
                    {
                        string outPath = opts.GetValueOrDefault("--out", "jira_training.jsonl");
                        await ExportJsonlAsync(dbPath, outPath);
                        Console.WriteLine($"Wrote {outPath}");
                        return 0;
                    }
                case "write-modelfile":
                    {
                        string outPath = opts.GetValueOrDefault("--out", "Modelfile");
                        File.WriteAllText(outPath, "FROM llama3.1:8b\nPARAMETER temperature 0.7\nFINETUNE jira_training.jsonl\n");
                        Console.WriteLine($"Wrote {outPath}");
                        return 0;
                    }
                case "finetune":
                    {
                        string name = opts.GetValueOrDefault("--name", "jira-bot");
                        string modelfile = opts.GetValueOrDefault("--modelfile", "Modelfile");
                        var startInfo = new System.Diagnostics.ProcessStartInfo("ollama", $"create {name} -f {modelfile}")
                        {
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        };
                        var p = System.Diagnostics.Process.Start(startInfo)!;
                        p.WaitForExit();
                        Console.WriteLine(p.StandardOutput.ReadToEnd());
                        Console.WriteLine(p.StandardError.ReadToEnd());
                        return p.ExitCode;
                    }
                default:
                    PrintHelp();
                    return 1;
            }
        }

        static async Task ExportJsonlAsync(string dbPath, string outPath)
        {
            var db = new Db(dbPath);
            var repo = new IssueRepo(db);
            var issues = repo.LoadAll();
            using var sw = new StreamWriter(outPath, false, new UTF8Encoding(false));
            foreach (var r in issues.Values)
            {
                var prompt1 = $"What is {r.Key}?";
                var response1 = $"Summary: {r.Summary}\nDescription: {r.Description}\nChildren: {string.Join(", ", r.Children)}\nRelated: {string.Join(", ", r.Relates)}";
                var obj1 = new { prompt = prompt1, response = response1 };
                await sw.WriteLineAsync(JsonConvert.SerializeObject(obj1));

                var prompt2 = $"Summarize issue {r.Key}";
                var response2 = $"{r.Key}: {r.Summary}";
                var obj2 = new { prompt = prompt2, response = response2 };
                await sw.WriteLineAsync(JsonConvert.SerializeObject(obj2));
            }
        }

        static Dictionary<string, string> ParseArgs(string[] arr)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < arr.Length; i++)
            {
                var a = arr[i];
                if (a.StartsWith("--"))
                {
                    string val = (i + 1 < arr.Length && !arr[i + 1].StartsWith("--")) ? arr[++i] : "true";
                    d[a] = val;
                }
            }
            return d;
        }

        static void PrintHelp()
        {
            Console.WriteLine(@"Jira + Ollama (Local) – RAG + optional finetune
Usage:
  index         --db monovera.sqlite [--chunksize 1500] [--hop 2] [--ollama http://localhost:11434]
  ask           --db monovera.sqlite --q ""question"" [--topk 12] [--seed DEV-123] [--sys ""style rules""] [--ollama http://localhost:11434]
  summary       --db monovera.sqlite --key DEV-123 [--ollama http://localhost:11434]
  tests         --db monovera.sqlite --key DEV-123 [--ollama http://localhost:11434]
  guide         --db monovera.sqlite --key DEV-123 [--ollama http://localhost:11434]
  export-jsonl  --db monovera.sqlite --out jira_training.jsonl
  write-modelfile --out Modelfile
  finetune      --name jira-bot --modelfile Modelfile");
        }
    }
}