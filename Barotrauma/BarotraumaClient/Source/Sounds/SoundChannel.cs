﻿using System;
using OpenTK.Audio.OpenAL;
using Microsoft.Xna.Framework;

namespace Barotrauma.Sounds
{
    public class SoundChannel : IDisposable
    {
        private const int STREAM_BUFFER_SIZE = 65536;

        private Vector3? position;
        public Vector3? Position
        {
            get { return position; }
            set
            {
                position = value;

                if (ALSourceIndex < 0) return;

                if (position != null)
                {
                    uint alSource = Sound.Owner.GetSourceFromIndex(ALSourceIndex);
                    AL.Source(alSource, ALSourceb.SourceRelative, true);
                    ALError alError = AL.GetError();
                    if (alError != ALError.NoError)
                    {
                        throw new Exception("Failed to enable source's relative flag: " + AL.GetErrorString(alError));
                    }

                    AL.Source(alSource, ALSource3f.Position, position.Value.X, position.Value.Y, position.Value.Z);
                    alError = AL.GetError();
                    if (alError != ALError.NoError)
                    {
                        throw new Exception("Failed to set source's position: " + AL.GetErrorString(alError));
                    }
                }
                else
                {
                    uint alSource = Sound.Owner.GetSourceFromIndex(ALSourceIndex);
                    AL.Source(alSource, ALSourceb.SourceRelative, false);
                    ALError alError = AL.GetError();
                    if (alError != ALError.NoError)
                    {
                        throw new Exception("Failed to disable source's relative flag: " + AL.GetErrorString(alError));
                    }

                    AL.Source(alSource, ALSource3f.Position, 0.0f, 0.0f, 0.0f);
                    alError = AL.GetError();
                    if (alError != ALError.NoError)
                    {
                        throw new Exception("Failed to reset source's position: " + AL.GetErrorString(alError));
                    }
                }
            }
        }

        private float near;
        public float Near
        {
            get { return near; }
            set
            {
                near = value;

                if (ALSourceIndex < 0) return;

                uint alSource = Sound.Owner.GetSourceFromIndex(ALSourceIndex);
                AL.Source(alSource, ALSourcef.ReferenceDistance, near);
                ALError alError = AL.GetError();
                if (alError != ALError.NoError)
                {
                    throw new Exception("Failed to set source's reference distance: " + AL.GetErrorString(alError));
                }
            }
        }

        private float far;
        public float Far
        {
            get { return far; }
            set
            {
                far = value;

                if (ALSourceIndex < 0) return;

                uint alSource = Sound.Owner.GetSourceFromIndex(ALSourceIndex);
                AL.Source(alSource, ALSourcef.MaxDistance, far);
                ALError alError = AL.GetError();
                if (alError != ALError.NoError)
                {
                    throw new Exception("Failed to set source's max distance: " + AL.GetErrorString(alError));
                }
            }
        }

        private float gain;
        public float Gain
        {
            get { return gain; }
            set
            {
                gain = Math.Max(Math.Min(value,1.0f),0.0f);

                if (ALSourceIndex < 0) return;

                uint alSource = Sound.Owner.GetSourceFromIndex(ALSourceIndex);
                AL.Source(alSource, ALSourcef.Gain, gain);
                ALError alError = AL.GetError();
                if (alError != ALError.NoError)
                {
                    throw new Exception("Failed to set source's gain: " + AL.GetErrorString(alError));
                }
            }
        }

        private bool looping;
        public bool Looping
        {
            get { return looping; }
            set
            {
                looping = value;

                if (ALSourceIndex < 0) return;

                if (!IsStream)
                {
                    uint alSource = Sound.Owner.GetSourceFromIndex(ALSourceIndex);
                    AL.Source(alSource, ALSourceb.Looping, looping);
                    ALError alError = AL.GetError();
                    if (alError != ALError.NoError)
                    {
                        throw new Exception("Failed to set source's looping state: " + AL.GetErrorString(alError));
                    }
                }
            }
        }

        public Sound Sound
        {
            get;
            private set;
        }

        public int ALSourceIndex
        {
            get;
            private set;
        }

        public bool IsStream
        {
            get;
            private set;
        }
        private int streamSeekPos;
        private bool startedPlaying;
        private bool reachedEndSample;
        private int[] streamBuffers;

        private object mutex;

        public bool IsPlaying
        {
            get
            {
                if (ALSourceIndex < 0) return false;
                if (IsStream && !reachedEndSample) return true;
                bool playing = AL.GetSourceState(Sound.Owner.GetSourceFromIndex(ALSourceIndex)) == ALSourceState.Playing;
                ALError alError = AL.GetError();
                if (alError != ALError.NoError)
                {
                    throw new Exception("Failed to determine playing state from source: "+AL.GetErrorString(alError));
                }
                return playing;
            }
        }

        public SoundChannel(Sound sound,float gain,Vector3? position,float near,float far)
        {
            Sound = sound;

            IsStream = sound.Stream;
            streamSeekPos = 0; reachedEndSample = false;
            startedPlaying = true;

            mutex = new object();

            ALSourceIndex = sound.Owner.AssignFreeSourceToChannel(this);

            if (ALSourceIndex>=0)
            {
                if (!IsStream)
                {
                    AL.BindBufferToSource(sound.Owner.GetSourceFromIndex(ALSourceIndex), (uint)sound.ALBuffer);
                    ALError alError = AL.GetError();
                    if (alError != ALError.NoError)
                    {
                        throw new Exception("Failed to bind buffer to source: " + AL.GetErrorString(alError));
                    }

                    AL.SourcePlay(sound.Owner.GetSourceFromIndex(ALSourceIndex));
                    alError = AL.GetError();
                    if (alError != ALError.NoError)
                    {
                        throw new Exception("Failed to play source: " + AL.GetErrorString(alError));
                    }
                }
                else
                {
                    streamBuffers = new int[4];
                    for (int i=0;i<4;i++)
                    {
                        streamBuffers[i] = AL.GenBuffer();

                        ALError alError = AL.GetError();
                        if (alError != ALError.NoError)
                        {
                            throw new Exception("Failed to generate stream buffers: " + AL.GetErrorString(alError));
                        }
                    }

                    Sound.Owner.InitStreamThread();
                }
            }

            this.Position = position;
            this.Gain = gain;
            this.Looping = false;
            this.Near = near;
            this.Far = far;
        }
        
        public void Dispose()
        {
            lock (mutex)
            {
                if (ALSourceIndex >= 0)
                {
                    AL.SourceStop(Sound.Owner.GetSourceFromIndex(ALSourceIndex));
                    ALError alError = AL.GetError();
                    if (alError != ALError.NoError)
                    {
                        throw new Exception("Failed to stop source: " + AL.GetErrorString(alError));
                    }
                
                    if (IsStream)
                    {
                        uint alSource = Sound.Owner.GetSourceFromIndex(ALSourceIndex);

                        AL.SourceStop(alSource);
                        alError = AL.GetError();
                        if (alError != ALError.NoError)
                        {
                            throw new Exception("Failed to stop streamed source: " + AL.GetErrorString(alError));
                        }

                        int buffersToUnqueue = 0;
                        int[] unqueuedBuffers = null;

                        buffersToUnqueue = 0;
                        AL.GetSource(alSource, ALGetSourcei.BuffersProcessed, out buffersToUnqueue);
                        alError = AL.GetError();
                        if (alError != ALError.NoError)
                        {
                            throw new Exception("Failed to determine processed buffers from streamed source: " + AL.GetErrorString(alError));
                        }

                        unqueuedBuffers = new int[buffersToUnqueue];
                        AL.SourceUnqueueBuffers((int)alSource, buffersToUnqueue, unqueuedBuffers);
                        alError = AL.GetError();
                        if (alError != ALError.NoError)
                        {
                            throw new Exception("Failed to unqueue buffers from streamed source: " + AL.GetErrorString(alError));
                        }

                        for (int i = 0; i < 4; i++)
                        {
                            AL.DeleteBuffer(streamBuffers[i]);
                            alError = AL.GetError();
                            if (alError != ALError.NoError)
                            {
                                throw new Exception("Failed to delete streamBuffers[" + i.ToString() + "]: " + AL.GetErrorString(alError));
                            }
                        }

                        reachedEndSample = true;
                    }
                    else
                    {
                        AL.BindBufferToSource(Sound.Owner.GetSourceFromIndex(ALSourceIndex), 0);
                        alError = AL.GetError();
                        if (alError != ALError.NoError)
                        {
                            throw new Exception("Failed to unbind buffer to non-streamed source: " + AL.GetErrorString(alError));
                        }
                    }

                    ALSourceIndex = -1;
                }
            }
        }

        public void UpdateStream()
        {
            if (!IsStream) throw new Exception("Called UpdateStream on a non-streamed sound channel!");

            lock (mutex)
            {
                if (!reachedEndSample)
                {
                    uint alSource = Sound.Owner.GetSourceFromIndex(ALSourceIndex);

                    bool playing = AL.GetSourceState(alSource) == ALSourceState.Playing;
                    ALError alError = AL.GetError();
                    if (alError != ALError.NoError)
                    {
                        throw new Exception("Failed to determine playing state from streamed source: " + AL.GetErrorString(alError));
                    }

                    int buffersToUnqueue = 0;
                    int[] unqueuedBuffers = null;
                    if (!startedPlaying)
                    {
                        buffersToUnqueue = 0;
                        AL.GetSource(alSource, ALGetSourcei.BuffersProcessed, out buffersToUnqueue);
                        alError = AL.GetError();
                        if (alError != ALError.NoError)
                        {
                            throw new Exception("Failed to determine processed buffers from streamed source: " + AL.GetErrorString(alError));
                        }

                        unqueuedBuffers = new int[buffersToUnqueue];
                        AL.SourceUnqueueBuffers((int)alSource, buffersToUnqueue, unqueuedBuffers);
                        alError = AL.GetError();
                        if (alError != ALError.NoError)
                        {
                            throw new Exception("Failed to unqueue buffers from streamed source: " + AL.GetErrorString(alError));
                        }
                    }
                    else
                    {
                        startedPlaying = false;
                        buffersToUnqueue = 4;
                        unqueuedBuffers = (int[])streamBuffers.Clone();
                    }

                    for (int i = 0; i < buffersToUnqueue; i++)
                    {
                        short[] buffer = new short[STREAM_BUFFER_SIZE];
                        int readSamples = Sound.FillStreamBuffer(streamSeekPos, buffer);
                        streamSeekPos += readSamples;
                        if (readSamples < STREAM_BUFFER_SIZE)
                        {
                            if (looping)
                            {
                                streamSeekPos = 0;
                            }
                            else
                            {
                                reachedEndSample = true;
                            }
                        }
                        if (readSamples > 0)
                        {
                            AL.BufferData<short>(unqueuedBuffers[i], Sound.ALFormat, buffer, readSamples, Sound.SampleRate);

                            alError = AL.GetError();
                            if (alError != ALError.NoError)
                            {
                                throw new Exception("Failed to assign data to stream buffer: " + AL.GetErrorString(alError));
                            }

                            AL.SourceQueueBuffer((int)alSource, unqueuedBuffers[i]);
                            alError = AL.GetError();
                            if (alError != ALError.NoError)
                            {
                                throw new Exception("Failed to queue buffer[" + i.ToString() + "] to stream: " + AL.GetErrorString(alError));
                            }
                        }
                    }

                    if (AL.GetSourceState(alSource) != ALSourceState.Playing)
                    {
                        AL.SourcePlay(alSource);
                    }
                }
            }
        }
    }
}
