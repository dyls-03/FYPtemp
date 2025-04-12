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

        static void RecordAudio(string filePath, int seconds = 5)
        {
            Console.WriteLine($"🎙️ Recording for {seconds} seconds...");

            using var waveIn = new WaveInEvent();
            waveIn.WaveFormat = new WaveFormat(44100, 1); // 44.1kHz mono
            using var writer = new WaveFileWriter(filePath, waveIn.WaveFormat);

            waveIn.DataAvailable += (s, a) => writer.Write(a.Buffer, 0, a.BytesRecorded);

            waveIn.StartRecording();
            Thread.Sleep(seconds * 1000);
            waveIn.StopRecording();

            Console.WriteLine("✅ Recording saved!");
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
