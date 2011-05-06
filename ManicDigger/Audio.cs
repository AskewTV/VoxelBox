﻿using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using OpenTK.Audio;
using System.Threading;
using System.Diagnostics;

namespace ManicDigger
{
    public interface IAudio
    {
        bool Enabled { get; set; }
        void Play(string filename);
        void PlayAudioLoop(string filename, bool play);
    }
    public class AudioDummy : IAudio
    {
        public void Play(string filename)
        {
        }
        public void PlayAudioLoop(string filename, bool play)
        {
        }
        bool enabled = true;
        public bool Enabled { get { return enabled; } set { enabled = value; } }
    }
    public class AudioOpenAl : IAudio
    {
        [Inject]
        public IGameExit d_GameExit;
        [Inject]
        public IGetFileStream d_GetFile;
        public AudioOpenAl()
        {
            try
            {
                IList<string> x = AudioContext.AvailableDevices;//only with this line an exception can be catched.
                context = new AudioContext();
            }
            catch (Exception e)
            {
                string oalinst = "oalinst.exe";
                if (File.Exists(oalinst))
                {
                    try
                    {
                        Process.Start(oalinst, "/s");
                    }
                    catch
                    {
                    }
                }
                Console.WriteLine(e);
            }
        }
        AudioContext context;
        /*
        static byte[] LoadOgg(Stream stream, out int channels, out int bits, out int rate)
        {
            byte[] bytes;
            Jarnbjo.Ogg.OggPage.Create(
            return bytes;
        }
        */
        // Loads a wave/riff audio file.
        public static byte[] LoadWave(Stream stream, out int channels, out int bits, out int rate)
        {
            if (stream == null)
                throw new ArgumentNullException("stream");

            using (BinaryReader reader = new BinaryReader(stream))
            {
                // RIFF header
                string signature = new string(reader.ReadChars(4));
                if (signature != "RIFF")
                    throw new NotSupportedException("Specified stream is not a wave file.");

                int riff_chunck_size = reader.ReadInt32();

                string format = new string(reader.ReadChars(4));
                if (format != "WAVE")
                    throw new NotSupportedException("Specified stream is not a wave file.");

                // WAVE header
                string format_signature = new string(reader.ReadChars(4));
                if (format_signature != "fmt ")
                    throw new NotSupportedException("Specified wave file is not supported.");

                int format_chunk_size = reader.ReadInt32();
                int audio_format = reader.ReadInt16();
                int num_channels = reader.ReadInt16();
                int sample_rate = reader.ReadInt32();
                int byte_rate = reader.ReadInt32();
                int block_align = reader.ReadInt16();
                int bits_per_sample = reader.ReadInt16();

                string data_signature = new string(reader.ReadChars(4));
                if (data_signature != "data")
                    throw new NotSupportedException("Specified wave file is not supported.");

                int data_chunk_size = reader.ReadInt32();

                channels = num_channels;
                bits = bits_per_sample;
                rate = sample_rate;

                return reader.ReadBytes((int)reader.BaseStream.Length);
            }
        }
        public static OpenTK.Audio.OpenAL.ALFormat GetSoundFormat(int channels, int bits)
        {
            switch (channels)
            {
                case 1: return bits == 8 ? OpenTK.Audio.OpenAL.ALFormat.Mono8 : OpenTK.Audio.OpenAL.ALFormat.Mono16;
                case 2: return bits == 8 ? OpenTK.Audio.OpenAL.ALFormat.Stereo8 : OpenTK.Audio.OpenAL.ALFormat.Stereo16;
                default: throw new NotSupportedException("The specified sound format is not supported.");
            }
        }
        Dictionary<string, AudioSample> cache = new Dictionary<string, AudioSample>();
        class AudioTask
        {
            public AudioTask(IGameExit gameexit, string id, AudioOpenAl audio)
            {
                this.gameexit = gameexit;
                this.filename = id;
                this.audio = audio;
            }
            AudioOpenAl audio;
            IGameExit gameexit;
            public string filename;
            public void Play()
            {
                if (started)
                {
                    shouldplay = true;
                    return;
                }
                started = true;
                new Thread(play).Start();
            }
            //bool resume = true;
            bool started = false;
            //static Dictionary<string, int> audiofiles = new Dictionary<string, int>();
            void play()
            {
                try
                {
                    DoPlay();
                }
                catch (Exception e) { Console.WriteLine(e.ToString()); }
            }
            

            AudioSample GetSample(string filename)
            {
                if (!audio.cache.ContainsKey(filename))
                {
                    Stream stream = audio.d_GetFile.GetFile(filename);
                    if (stream.ReadByte() == 'R'
                        && stream.ReadByte() == 'I'
                        && stream.ReadByte() == 'F'
                        && stream.ReadByte() == 'F')
                    {
                        stream.Position = 0;
                        int channels, bits_per_sample, sample_rate;
                        byte[] sound_data = LoadWave(stream, out channels, out bits_per_sample, out sample_rate);
                        AudioSample sample = new AudioSample()
                        {
                            Pcm = sound_data,
                            BitsPerSample = bits_per_sample,
                            Channels = channels,
                            Rate = sample_rate,
                        };
                        audio.cache[filename] = sample;
                    }
                    else
                    {
                        stream.Position = 0;
                        AudioSample sample = new OggDecoder().OggToWav(stream);
                        audio.cache[filename] = sample;
                    }
                }
                return audio.cache[filename];
            }

            private void DoPlay()
            {
                AudioSample sample = GetSample(filename);
                //if(!audiofiles.ContainsKey(filename))
                {

                }
                int source = OpenTK.Audio.OpenAL.AL.GenSource();
                int state;
                //using ()
                {
                    //Trace.WriteLine("Testing WaveReader({0}).ReadToEnd()", filename);

                    int buffer = OpenTK.Audio.OpenAL.AL.GenBuffer();

                    OpenTK.Audio.OpenAL.AL.BufferData(buffer, GetSoundFormat(sample.Channels, sample.BitsPerSample), sample.Pcm, sample.Pcm.Length, sample.Rate);
                    //audiofiles[filename]=buffer;

                    OpenTK.Audio.OpenAL.AL.Source(source, OpenTK.Audio.OpenAL.ALSourcei.Buffer, buffer);
                    OpenTK.Audio.OpenAL.AL.SourcePlay(source);

                    // Query the source to find out when it stops playing.
                    for (; ; )
                    {
                        OpenTK.Audio.OpenAL.AL.GetSource(source, OpenTK.Audio.OpenAL.ALGetSourcei.SourceState, out state);
                        if ((!loop) && (OpenTK.Audio.OpenAL.ALSourceState)state != OpenTK.Audio.OpenAL.ALSourceState.Playing)
                        {
                            break;
                        }
                        if (gameexit.exit)
                        {
                            break;
                        }
                        if (loop)
                        {
                            if (state == (int)OpenTK.Audio.OpenAL.ALSourceState.Playing && (!shouldplay))
                            {
                                OpenTK.Audio.OpenAL.AL.SourcePause(source);
                            }
                            if (state != (int)OpenTK.Audio.OpenAL.ALSourceState.Playing && (shouldplay))
                            {
                                OpenTK.Audio.OpenAL.AL.SourcePlay(source);
                            }
                        }
                        /*
                        if (stop)
                        {
                            AL.SourcePause(source);
                            resume = false;
                        }
                        if (resume)
                        {
                            AL.SourcePlay(source);
                            resume = false;
                        }
                        */
                        Thread.Sleep(1);
                    }
                    OpenTK.Audio.OpenAL.AL.SourceStop(source);
                    OpenTK.Audio.OpenAL.AL.DeleteSource(source);
                    OpenTK.Audio.OpenAL.AL.DeleteBuffer(buffer);
                }
            }
            public bool loop = false;
            //bool stop;
            //public void Stop()
            //{
            //    stop = true;
            //}
            public bool shouldplay;
        }
        public void Play(string filename)
        {
            if (!enabled)
            {
                return;
            }
            if (context == null)
            {
                return;
            }
            new AudioTask(d_GameExit, filename, this).Play();
        }
        Dictionary<string, AudioTask> soundsplaying = new Dictionary<string, AudioTask>();
        public void PlayAudioLoop(string filename, bool play)
        {
            if (!enabled)
            {
                return;
            }
            if (context == null)
            {
                return;
            }
            //todo: resume playing.
            if (play)
            {
                if (!soundsplaying.ContainsKey(filename))
                {
                    var x = new AudioTask(d_GameExit, filename, this);
                    x.loop = true;
                    soundsplaying[filename] = x;
                }
                soundsplaying[filename].Play();
            }
            else
            {
                if (soundsplaying.ContainsKey(filename))
                {
                    soundsplaying[filename].shouldplay = false;
                    //soundsplaying.Remove(filename);
                }
            }
        }
        bool enabled = true;
        public bool Enabled { get { return enabled; } set { enabled = value; } }
    }
}
