using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NAudio.Wave;
using System.Text.RegularExpressions;


namespace gptTestApp
{
    class Program
    {
        static bool shouldExit = false;
        static ManualResetEvent triggerDetected = new(false);
        static bool forceTrigger = false;

        static async Task Main(string[] args)
        {
            Console.Write("Enter your OpenAI API key: ");
            string apiKey = Console.ReadLine();

            Console.Write("Proceed with voice assistant? (y/n): ");
            var confirm = Console.ReadKey();
            Console.WriteLine();
            if (confirm.Key != ConsoleKey.Y) return;

            Console.WriteLine("[INFO] BB is now listening... Press Escape to quit.\n");

            // Escape key monitor
            Task.Run(() =>
            {
                while (!shouldExit)
                {
                    if (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(true).Key;
                        if (key == ConsoleKey.Escape)
                            shouldExit = true;
                        else if (key == ConsoleKey.R)
                            forceTrigger = true;
                    }
                }
            });

            while (!shouldExit)
            {
                string audioPath = "speech.wav";

                if (forceTrigger)
                {
                    // --- OVERRIDE PATH ---
                    Console.WriteLine("[OVERRIDE] Button override activated.");
                    Console.WriteLine("[OVERRIDE] Please ask your question...");

                    forceTrigger = false;

                    RecordAudio(audioPath);

                    string transcript = await TranscribeAudioAsync(audioPath, apiKey);
                    if (string.IsNullOrWhiteSpace(transcript))
                    {
                        Console.WriteLine("[ERROR] No voice input detected.");
                        continue;
                    }

                    Console.WriteLine($"\n[TRANSCRIPT] \"{transcript}\"");

                    string reply = await GetChatGPTResponseAsync(apiKey, transcript.Trim());
                    Console.WriteLine($"\n[BB]: {reply}\n");
                }
                else
                {
                    // --- NORMAL FLOW (trigger required) ---
                    Console.WriteLine("[WAIT] Say something (waiting for trigger phrase: 'Hey BB')...");

                    RecordAudio(audioPath);

                    string transcript = await TranscribeAudioAsync(audioPath, apiKey);
                    if (string.IsNullOrWhiteSpace(transcript))
                    {
                        Console.WriteLine("[ERROR] No voice input detected.");
                        continue;
                    }

                    Console.WriteLine($"\n[TRANSCRIPT] \"{transcript}\"");

                    if (TriggerDetected(transcript))
                    {
                        string cleanedInput = Regex.Replace(transcript, @"\b(hey\s+bb|hello\s+bb|bb)\b", "", RegexOptions.IgnoreCase).Trim();

                        if (string.IsNullOrWhiteSpace(cleanedInput))
                        {
                            Console.WriteLine("[NOTICE] Trigger phrase detected, but no question was asked.");
                            continue;
                        }

                        string reply = await GetChatGPTResponseAsync(apiKey, cleanedInput);
                        Console.WriteLine($"\n[BB]: {reply}\n");
                    }
                    else
                    {
                        Console.WriteLine("[INFO] No trigger phrase detected. Listening again...\n");
                    }
                }
            }





            Console.WriteLine("[EXIT] Exiting BB assistant. Goodbye!");



        }


        static void RecordAudio(string filePath, int silenceThreshold = 200, int silenceDurationMs = 2500)
        {
            Console.WriteLine("[INFO] Start speaking. Recording will stop when you're silent...");

            using var waveIn = new WaveInEvent();
            waveIn.WaveFormat = new WaveFormat(44100, 1);
            using var writer = new WaveFileWriter(filePath, waveIn.WaveFormat);

            int silentFor = 0;
            int checkInterval = 100; // ms
            bool isSilent = false;

            var stopwatch = new System.Diagnostics.Stopwatch();

            waveIn.DataAvailable += (s, a) =>
            {
                writer.Write(a.Buffer, 0, a.BytesRecorded);

                // Measure RMS level
                float sum = 0;
                for (int i = 0; i < a.BytesRecorded; i += 2)
                {
                    short sample = BitConverter.ToInt16(a.Buffer, i);
                    sum += Math.Abs(sample);
                }
                float average = sum / (a.BytesRecorded / 2);

                // Detect silence
                if (average < silenceThreshold)
                {
                    if (!isSilent)
                    {
                        isSilent = true;
                        stopwatch.Restart();
                    }
                }
                else
                {
                    isSilent = false;
                    stopwatch.Reset();
                }
            };

            waveIn.StartRecording();

            while (true)
            {
                Thread.Sleep(checkInterval);
                if (isSilent && stopwatch.ElapsedMilliseconds >= silenceDurationMs)
                {
                    break;
                }
            }

            waveIn.StopRecording();
            Console.WriteLine("[INFO] Recording stopped due to silence.");
        }


        static async Task<string> GetChatGPTResponseAsync(string apiKey, string userInput)
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var requestBody = new
            {
                model = "gpt-4o-mini",
                messages = new[]
                {
                    new { role = "system", content = "You are BB, short for Black Box — a cheerful and enthusiastic AI assistant for a university open day. You always respond in an upbeat, friendly tone. Your job is to answer any question without ever asking questions in return. Stay helpful, positive, and clear, but never ask the user anything back." },
                    new { role = "user", content = userInput }
                }
            };

            string jsonBody = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            var response = await httpClient.PostAsync("https://api.openai.com/v1/chat/completions", content);
            var responseContent = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(responseContent);
            return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        }



        static async Task<string> TranscribeAudioAsync(string filePath, string apiKey)
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            using var form = new MultipartFormDataContent();
            form.Add(new StringContent("whisper-1"), "model");
            form.Add(new ByteArrayContent(File.ReadAllBytes(filePath)), "file", "speech.wav");

            var response = await client.PostAsync("https://api.openai.com/v1/audio/transcriptions", form);
            var responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine("[ERROR] Whisper API Error:");
                Console.WriteLine(responseText);
                return null;
            }

            using var json = JsonDocument.Parse(responseText);
            return json.RootElement.GetProperty("text").GetString();
        }

        static bool TriggerDetected(string input)
        {
            var pattern = @"\b(hey\s+bb|hello\s+bb|bb|help|hey\s+black\s+box|hello\s+black\s+box)\b";
            return Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase);
        }
    }
}
