using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace Monovera
{
    internal class AppLogger
    {
        private static readonly string LogFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log");

        public static void Log(string description, [CallerMemberName] string methodName = "")
        {
            try
            {
                if (!Directory.Exists(LogFolder))
                    Directory.CreateDirectory(LogFolder);

                string user = Monovera.frmMain.jiraUserName ?? "unknown";
                string logFile = Path.Combine(LogFolder, $"{DateTime.Now:yyyy-MM-dd}.log");
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string line = $"{timestamp}\t{user}\t{methodName}\t{description}";

                File.AppendAllText(logFile, line + Environment.NewLine);
            }
            catch
            {
                // Optionally handle logging errors
            }
        }
    }
}
