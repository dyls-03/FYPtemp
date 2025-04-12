using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NAudio.Wave;


namespace gptTestApp
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("Enter your OpenAI API key:");
            string apiKey = Console.ReadLine();
            string audioPath = "speech.wav";

            RecordAudio(audioPath);
            string userInput = await TranscribeAudioAsync(audioPath, apiKey);

            if (string.IsNullOrWhiteSpace(userInput))
            {
                Console.WriteLine("❌ No transcribed text.");
                return;
            }

            Console.WriteLine($"\nYou said: {userInput}");

            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var requestBody = new
            {
                model = "gpt-3.5-turbo",
                messages = new[]
                {
                    new { role = "system", content = "You are BB, short for Black Box — a cheerful and enthusiastic AI assistant for a university open day. You always respond in an upbeat, friendly tone. Your job is to answer any question without ever asking questions in return. Stay helpful, positive, and clear, but never ask the user anything back." },
                    new { role = "user", content = userInput }
                }
            };

            string jsonBody = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            var response = await httpClient.PostAsync("https://api.openai.com/v1/chat/completions", content);
            string responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine("❌ ChatGPT Error:");
                Console.WriteLine(responseContent);
                return;
            }

            using var doc = JsonDocument.Parse(responseContent);
            string reply = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();

            Console.WriteLine($"\nBB: {reply}");



        }

        static void RecordAudio(string filePath, int silenceThreshold = 300, int silenceDurationMs = 2000)
        {
            Console.WriteLine("🎙️ Start speaking. Recording will stop when you're silent...");

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
            Console.WriteLine("✅ Recording stopped due to silence.");
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
                Console.WriteLine("❌ Whisper API Error:");
                Console.WriteLine(responseText);
                return null;
            }

            using var json = JsonDocument.Parse(responseText);
            return json.RootElement.GetProperty("text").GetString();
        }
    }
}
