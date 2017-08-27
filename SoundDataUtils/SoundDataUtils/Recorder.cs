using System;
using System.Linq;
using System.Threading;
using CSCore;
using CSCore.Codecs.WAV;
using CSCore.CoreAudioAPI;
using CSCore.SoundIn;
using CSCore.Streams;

namespace SoundDataUtils
{
    public static class Recorder
    {
        public static void RecordTo(string fileName, TimeSpan time, WaveFormat format)
        {
            CaptureMode captureMode = CaptureMode.Capture;
            DataFlow dataFlow = captureMode == CaptureMode.Capture ? DataFlow.Capture : DataFlow.Render;

            var devices = MMDeviceEnumerator.EnumerateDevices(dataFlow, DeviceState.Active);
            var device = devices.FirstOrDefault();

            using (WasapiCapture soundIn = captureMode == CaptureMode.Capture
                ? new WasapiCapture()
                : new WasapiLoopbackCapture())
            {
                soundIn.Device = device;
                soundIn.Initialize();
                SoundInSource soundInSource = new SoundInSource(soundIn) { FillWithZeros = false };
                IWaveSource convertedSource = soundInSource
                    .ChangeSampleRate(format.SampleRate) // sample rate
                    .ToSampleSource()
                    .ToWaveSource(format.BitsPerSample); //bits per sample
                using (convertedSource = format.Channels == 1 ? convertedSource.ToMono() : convertedSource.ToStereo())
                {
                    using (WaveWriter waveWriter = new WaveWriter(fileName, convertedSource.WaveFormat))
                    {
                        soundInSource.DataAvailable += (s, e) =>
                        {
                            byte[] buffer = new byte[convertedSource.WaveFormat.BytesPerSecond / 2];
                            int read;
                            while ((read = convertedSource.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                waveWriter.Write(buffer, 0, read);
                            }
                        };

                        soundIn.Start();

                        Console.WriteLine("Started recording");
                        Thread.Sleep(time);

                        soundIn.Stop();
                        Console.WriteLine("Finished recording");
                    }
                }
            }
        }
    }
}