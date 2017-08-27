using System;
using System.Collections.Generic;
using System.Linq;
using CSCore;
using CSCore.MediaFoundation;

namespace SoundDataUtils
{
    public static class AudioToText
    {
        public static IEnumerable<byte[]> GetByteChunks(this string base64String, char separator = ' ')
        {

            var samples64 = base64String.Split(separator);
            foreach (var element in samples64.Select(s => Convert.FromBase64String(s)))
            {
                yield return element;
            }

        }

        public static string Tob64String(this IWaveSource source, WaveFormat format)
        {
            var listOfChunks = string.Empty;
            int ctr = 0;

            byte[] buffer = new byte[format.BytesPerSecond / format.SampleRate];
            int read;

            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                var base64 = Convert.ToBase64String(buffer);
                listOfChunks += base64;
                if (read == buffer.Length)
                    listOfChunks += ' ';
                if (ctr++ % 1000 == 0)
                    Console.WriteLine(Math.Round((source.Position / (double)source.Length) * 100));
            }
            return listOfChunks;

        }
        public static void ToAudioAgain(string base64Chunks, string outputFile, WaveFormat wav)
        {

            using (var encoder = MediaFoundationEncoder.CreateMP3Encoder(wav, outputFile))
            {
                foreach (var element in base64Chunks.GetByteChunks())
                {
                    encoder.Write(element, 0, element.Length);
                }
            }
        }
    }
}