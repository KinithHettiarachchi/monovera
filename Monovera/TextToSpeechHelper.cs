using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System;
using System.IO;
using System.Media;
using NAudio.Wave;

namespace Monovera
{
    class TextToSpeechHelper
    {
        public static void SpeakWithWindowsDefaultVoice(string text)
        {
            // Escape single quotes and line breaks for PowerShell compatibility
            string escapedText = text
                .Replace("'", "''")
                .Replace("\r", " ")
                .Replace("\n", " ");

            string script = $@"
Add-Type -AssemblyName System.Speech
$synth = New-Object -TypeName System.Speech.Synthesis.SpeechSynthesizer
$synth.SelectVoice('Microsoft Zira Desktop')
$synth.Rate = -1
$synth.Speak(@'
{escapedText}
'@)
";

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-NoLogo -NoProfile -Command \"{script}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            };

            System.Diagnostics.Process.Start(psi);
        }

        public static void SpeakWithGoogleDefaultVoice(string text, string languageCode = "en-GB")
        {
            string preparedText = PrepareTextForTTS(text);
            bool success = false;

            // Optional: chunk if text is too long (e.g. >100 chars)
            foreach (var chunk in SplitTextIntoChunks(preparedText, 200))
            {
                success = PlayTTSChunk(chunk, languageCode);
                if (!success)
                {
                    break;
                }
            }
        }

        private static bool PlayTTSChunk(string textChunk, string languageCode)
        {
            if (string.IsNullOrEmpty(textChunk)) { return true; }
            string encodedText = HttpUtility.UrlEncode(textChunk);
            string url = $"https://translate.google.com/translate_tts?ie=UTF-8&q={encodedText}&tl={languageCode}&client=tw-ob";

            string tempMp3Path = Path.Combine(Path.GetTempPath(), "tts_temp.mp3");

            try
            {
                using (var client = new WebClient())
                {
                    client.Headers.Add("User-Agent", "Mozilla/5.0");
                    client.DownloadFile(url, tempMp3Path);
                }
            }
            catch (Exception ex)
            {
                SpeakWithGoogleDefaultVoice("Oops! Something went wrong! can you please try again?");
                return false;
            }
           

            PlayMp3(tempMp3Path);
            return true;
        }

        public static void PlayMp3(string path)
        {
            try
            {
                using (var audioFile = new AudioFileReader(path))
                using (var outputDevice = new WaveOutEvent())
                {
                    outputDevice.Init(audioFile);
                    outputDevice.Play();

                    // Wait until playback finishes
                    while (outputDevice.PlaybackState == PlaybackState.Playing)
                    {
                        System.Threading.Thread.Sleep(100);
                    }
                }
            }
            catch (Exception ex)
            {
               // SpeakWithGoogleDefaultVoice("Oops! Something went wrong! can you please try again?");
            }
            //delete file after playback
            File.Delete(path);
        }

        public static string PrepareTextForTTS(string text)
        {
            // 1. Normalize line breaks and trim
            string cleaned = text.Replace("\r", " ").Replace("\n", " ").Trim();

            // 2. Collapse multiple whitespaces into a single space
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s+", " ");

            // 3. Remove or replace problematic characters (e.g., square brackets, percent signs)
            cleaned = cleaned.Replace("[", "")
                             .Replace("]", "")
                             .Replace("%", " percent")
                             .Replace(":", "")
                             .Replace("–", "-");

            return cleaned;
        }

        public static List<string> SplitTextIntoChunks(string text, int maxChunkSize = 200)
        {
            var chunks = new List<string>();
            var currentChunk = new StringBuilder();

            foreach (string sentence in text.Split(new[] { ". " }, StringSplitOptions.None))
            {
                if (currentChunk.Length + sentence.Length + 2 <= maxChunkSize)
                {
                    currentChunk.Append(sentence).Append(". ");
                }
                else
                {
                    if (currentChunk.Length > 0)
                    {
                        chunks.Add(currentChunk.ToString().Trim());
                        currentChunk.Clear();
                    }

                    // If sentence is too long itself, split by space
                    if (sentence.Length > maxChunkSize)
                    {
                        string[] words = sentence.Split(' ');
                        foreach (var word in words)
                        {
                            if (currentChunk.Length + word.Length + 1 <= maxChunkSize)
                            {
                                currentChunk.Append(word).Append(" ");
                            }
                            else
                            {
                                chunks.Add(currentChunk.ToString().Trim());
                                currentChunk.Clear();
                                currentChunk.Append(word).Append(" ");
                            }
                        }
                    }
                    else
                    {
                        currentChunk.Append(sentence).Append(". ");
                    }
                }
            }

            if (currentChunk.Length > 0)
            {
                chunks.Add(currentChunk.ToString().Trim());
            }

            return chunks;
        }

    }
}
