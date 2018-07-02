using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CSCore;
using CSCore.Codecs;
using CSCore.Codecs.WAV;
using CSCore.CoreAudioAPI;
using CSCore.SoundIn;
using CSCore.SoundOut;
using CSCore.Streams;
using SoundDataUtils;

namespace Sample_NoiseDetector
{
    class Program
    {
        static void Main(string[] args)
        {
            var thresholdedLevel = 90;

            var circularBuffer = new ConcuurentCircularBuffer<double>(20);

            bool ShouldScream()
            {
                var currentBuffer = circularBuffer.Read();
                var result = currentBuffer.Average() > thresholdedLevel;
                Console.WriteLine(result);

                return result;
            }

            void UpdateState(double current)
            {
                //If already reached threshold stop adding 
                circularBuffer.Put(ShouldScream() ? thresholdedLevel / 2 : current);
            }

            var th = new Thread(() =>
            {
                while (true)
                {
                    Player.Play("aaa.mp3", () => !ShouldScream());
                    Thread.Sleep(1000);
                }
            });
            th.Start();

            var noiseInfo = new NoiseInfo();
            var noiseState = Observable.FromEventPattern<NoiseInfoEventArgs>(
                h => noiseInfo.OnNoiseData += h,
                h => noiseInfo.OnNoiseData -= h)
                .Select(e => e.EventArgs);
            noiseState
                .Buffer(50).Select(b => b.Average(i => i.Decibels))
                .Subscribe(averageSoundLevel =>
                {
                    UpdateState(averageSoundLevel);

                    LogToConsole(averageSoundLevel, thresholdedLevel);
                });
            noiseInfo.OnStopped += (es, e) =>
            {
                Console.WriteLine("Stopped");

                noiseInfo.Start();
            };
            noiseInfo.Start();
            th.Join();

        }

        private static void LogToConsole(double averageSoundLevel, int thresholdedLevel)
        {
            var levelString = new string('=', Console.WindowWidth * (int)averageSoundLevel / 100);
            if (levelString.Length > thresholdedLevel)
            {
                var newStringCharArray = levelString.ToCharArray();
                newStringCharArray[thresholdedLevel] = '|';
                var newString = new string(newStringCharArray);
                levelString = newString;
            }

            Console.WriteLine(levelString);
        }
    }
    public interface ICircularBuffer<T>
    {
        void Put(T item);  // put an item
        T[] Read(); // provides the last "n" requests
    }
    public class ConcuurentCircularBuffer<T> : ICircularBuffer<T>
    {

        private T[] buffer;
        private int last = 0;
        private int sz;
        private object lockObject = new object();

        public ConcuurentCircularBuffer(int sz)
        {
            // array index starts at 1
            this.sz = sz;
            buffer = new T[sz + 1];
        }

        public void Put(T item)
        {
            lock (lockObject)
            {
                last++;
                last = last > sz ? 1 : last;
                buffer[last] = item;
            }
        }

        public T[] Read()
        {
            T[] arr = new T[sz];

            lock (lockObject)
            {
                int iterator = 0;
                for (int read = 0; read < sz; read++)
                {
                    int index = last - iterator;
                    index = index <= 0 ? (sz + index) : index;
                    if (buffer[index] != null)
                    {
                        arr[iterator] = buffer[index];
                    }
                    else
                    {
                        break;
                    }
                    iterator++;
                }
            }
            return arr;
        }
    }

    public class NoiseInfo
    {
        public event EventHandler<NoiseInfoEventArgs> OnNoiseData;
        public event EventHandler<EventArgs> OnStopped;
        public event EventHandler<EventArgs> OnStarted;
        readonly object timeoutLocker = new object();
        public void Start()
        {

            int sampleRate = 48000;
            int bitsPerSample = 24;

            MMDeviceCollection devices;
            while (!(devices = MMDeviceEnumerator.EnumerateDevices(DataFlow.Capture, DeviceState.Active)).Any())
            {
                Thread.Sleep(2000);
            }
            var device = devices.FirstOrDefault();

            //create a new soundIn instance
            using (WasapiCapture soundIn = new WasapiCapture())
            {
                soundIn.Device = device;
                //initialize the soundIn instance
                soundIn.Initialize();

                //create a SoundSource around the the soundIn instance
                SoundInSource soundInSource = new SoundInSource(soundIn) { FillWithZeros = false };

                //create a source, that converts the data provided by the soundInSource to any other format

                IWaveSource convertedSource = soundInSource
                    .ChangeSampleRate(sampleRate) // sample rate
                    .ToSampleSource()
                    .ToWaveSource(bitsPerSample); //bits per sample

                using (var stream = new MemoryStream())
                {
                    var readBufferLength = convertedSource.WaveFormat.BytesPerSecond / 2;
                    //channels...
                    using (convertedSource = convertedSource.ToStereo())
                    {
                        //create a new wavefile
                        using (WaveWriter waveWriter = new WaveWriter(stream, convertedSource.WaveFormat))
                        {
                            //register an event handler for the DataAvailable event of the soundInSource
                            soundInSource.DataAvailable += (s, e) =>
                            {
                                //read data from the converedSource
                                byte[] buffer = new byte[readBufferLength];
                                int read;

                                //keep reading as long as we still get some data
                                while ((read = convertedSource.Read(buffer, 0, buffer.Length)) > 0)
                                {

                                    var decibelsCalibrated = (int)Math.Round(GetDecibels(buffer, 30, 20, 140));
                                    if (decibelsCalibrated < 0)
                                        decibelsCalibrated = 0;
                                    OnNoiseData?.Invoke(null, new NoiseInfoEventArgs() { Decibels = decibelsCalibrated });
                                    //write the read data to a file
                                    waveWriter.Write(buffer, 0, read);
                                }
                            };
                            soundIn.Stopped += (e, args) =>
                            {

                                OnStopped?.Invoke(null, null);
                                lock (timeoutLocker)
                                    Monitor.PulseAll(timeoutLocker);
                            };

                            //start recording
                            soundIn.Start();
                            OnStarted?.Invoke(null, null);
                            Monitor.Enter(timeoutLocker);
                            {
                                Monitor.PulseAll(timeoutLocker);
                                Monitor.Wait(timeoutLocker);
                            }
                            //stop recording
                            soundIn.Stop();
                        }
                    }
                }
            }
        }

        public static double GetDecibels(byte[] playBuffer, double calibrateAdd, double calibratescale, double maxDecibels)
        {
            try
            {
                double sum = 0;
                for (var i = 0; i < playBuffer.Length; i = i + 2)
                {
                    double sample = BitConverter.ToInt16(playBuffer, i) / 32768.0;
                    sum += (sample * sample);
                }

                double rms = Math.Sqrt(sum / (playBuffer.Length / 2));
                var soundLevel = 20 * Math.Log10(rms);
                soundLevel += calibrateAdd;
                soundLevel *= calibratescale;
                if (soundLevel < 0) soundLevel = 0;
                if (soundLevel > maxDecibels) soundLevel = maxDecibels;
                return soundLevel / maxDecibels * 100;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }


    }

    public class NoiseInfoEventArgs : EventArgs
    {
        public int Decibels { get; set; }
    }
}
