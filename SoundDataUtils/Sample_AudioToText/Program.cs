using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CSCore;
using CSCore.Codecs;
using SoundDataUtils;

namespace Sample_AudioToText
{
    class Program
    {
        static void Main(string[] args)
        {
            var fileName = DateTime.Now.ToString("yyMMddHHMMss") + ".wav";
            var txtFile = fileName + ".txt";
            var outputFile = fileName + ".random.mp3";
            var audioLength = TimeSpan.FromSeconds(10);

            //NOTE! commented out values are better quality configurations but it generates audio which is 10 times larger in size
            //So I chose lowest hearable quality to reduce processing time to 1/10 of this standard (good) quality audio

            int sampleRate = 8000;// 44100;
            int bitsPerSample = 16;// 16;
            int channels = 1;// 2;

            var wav = new WaveFormat(sampleRate, bitsPerSample, channels);


            Recorder.RecordTo(fileName, audioLength, wav);
            Console.WriteLine("Playing initial audio");
            Player.Play(fileName);

            Console.WriteLine("Converting audio to base64 string");
            var tstring = CodecFactory.Instance.GetCodec(fileName).Tob64String(wav);

            Console.WriteLine("Writing converted base64 string:");

            Console.WriteLine(tstring);

            //Now write this text to file and read from it to ensure data consistency

            //You can use ASCII encoding for smaller size text file 
            var textEncoding = Encoding.ASCII;
            File.WriteAllText(txtFile, tstring, textEncoding);
            var allBase64s = File.ReadAllText(txtFile, textEncoding);

            AudioToText.ToAudioAgain(allBase64s, outputFile, wav);

            Player.Play(outputFile);

        }
    }
}
