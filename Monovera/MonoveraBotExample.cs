using System;
using System.Threading.Tasks;

namespace Monovera
{
    /// <summary>
    /// Example usage of MonoveraBot with AI-powered responses.
    /// Now uses Ollama for ChatGPT-like intelligent answers!
    /// </summary>
    public class MonoveraBotExample
    {
        public static async Task RunExample(string databasePath)
        {
            // Initialize the bot with your database path (uses Ollama phi3 by default)
            using var bot = new MonoveraBot(databasePath);

            Console.WriteLine("=== MonoveraBot with AI Example ===\n");
            Console.WriteLine("ℹ️  Make sure Ollama is installed and running: https://ollama.ai");
            Console.WriteLine("ℹ️  Install model: ollama pull phi\n");

            // Check if already trained
            if (!bot.IsTrained)
            {
                Console.WriteLine("Training the bot...");

                // Train with progress reporting
                var progress = new Progress<string>(message => Console.WriteLine($"[Training] {message}"));
                await bot.TrainAsync(progress);

                Console.WriteLine("Training completed!\n");
            }
            else
            {
                Console.WriteLine("Bot is already trained. Loading existing knowledge base...\n");
            }

            // Ask questions and get AI-powered responses!
            Console.WriteLine("=== Asking Questions (AI-Powered Responses) ===\n");

            await AskQuestion(bot, "What test cases are related to login?");
            await AskQuestion(bot, "Why did the authentication feature fail?");
            await AskQuestion(bot, "How do I test the password reset flow?");
            await AskQuestion(bot, "Summarize all issues in the TST project");
        }

        private static async Task AskQuestion(MonoveraBot bot, string question)
        {
            Console.WriteLine($"🤔 Q: {question}");
            Console.WriteLine("💭 AI thinking...");

            string answer = await bot.AskAsync(question);
            Console.WriteLine($"🤖 A: {answer}\n");
            Console.WriteLine(new string('-', 80) + "\n");
        }

        // Example: Using a different model
        public static async Task RunWithCustomModel(string databasePath)
        {
            // Use a different Ollama model
            var llmProvider = new OllamaLlmProvider(modelName: "llama3.2:3b");
            using var bot = new MonoveraBot(databasePath, llmProvider: llmProvider);

            await bot.TrainAsync();

            var answer = await bot.AskAsync("Explain the testing architecture");
            Console.WriteLine(answer);
        }

        // Synchronous example (still works, but now uses AI!)
        public static void RunSyncExample(string databasePath)
        {
            using var bot = new MonoveraBot(databasePath);

            if (!bot.IsTrained)
            {
                Console.WriteLine("Training the bot...");
                bot.Train();
                Console.WriteLine("Training completed!\n");
            }

            // Simple synchronous usage (blocks until AI responds)
            string answer = bot.Ask("What are the pending issues?");
            Console.WriteLine(answer);
        }

        // Interactive chat example
        public static async Task RunInteractiveChat(string databasePath)
        {
            using var bot = new MonoveraBot(databasePath);

            if (!bot.IsTrained)
            {
                Console.WriteLine("Training bot...");
                await bot.TrainAsync(new Progress<string>(Console.WriteLine));
            }

            Console.WriteLine("\n=== Interactive MonoveraBot Chat ===");
            Console.WriteLine("Ask questions about your issues, tests, and requirements.");
            Console.WriteLine("Type 'exit' to quit.\n");

            while (true)
            {
                Console.Write("\nYou: ");
                var question = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(question) || 
                    question.Equals("exit", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                Console.WriteLine("Bot: [AI thinking...]");
                var answer = await bot.AskAsync(question);
                Console.WriteLine($"Bot: {answer}");
            }

            Console.WriteLine("\nGoodbye!");
        }
    }
}
