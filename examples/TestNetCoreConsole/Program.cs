// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Drawing;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.MixedReality.WebRTC;

namespace TestNetCoreConsole
{
    class Program

    {
        static unsafe void AudioCallback (in AudioFrameRequest request)
        {
            var data = stackalloc ushort[480];

            for (int i = 0; i < 480; i += 1) {
                data[i] = (ushort)((Math.Sin((double)i / 480 * 2 * Math.PI)+1) / 2 * 0xFFFF);
            }

            request.CompleteRequest(new AudioFrame { 
                channelCount = 1, 
                bitsPerSample = 16, 
                sampleCount = 480, 
                sampleRate = 48000, 
                audioData = new IntPtr(data) 
            });
        }

        static async Task Main(string[] args)
        {
            Transceiver audioTransceiver = null;
            Transceiver videoTransceiver = null;
            LocalAudioTrack localAudioTrack = null;
            LocalVideoTrack localVideoTrack = null;

            try
            {
                bool needVideo = Array.Exists(args, arg => (arg == "-v") || (arg == "--video"));
                bool needAudio = Array.Exists(args, arg => (arg == "-a") || (arg == "--audio"));

                // Asynchronously retrieve a list of available video capture devices (webcams).
                var deviceList = await PeerConnection.GetVideoCaptureDevicesAsync();

                // For example, print them to the standard output
                foreach (var device in deviceList)
                {
                   Console.WriteLine($"Found webcam {device.name} (id: {device.id})");
                }

                // Create a new peer connection automatically disposed at the end of the program
                using var pc = new PeerConnection();

                // Initialize the connection with a STUN server to allow remote access
                var config = new PeerConnectionConfiguration
                {
                    IceServers = new List<IceServer> {
                            new IceServer{ Urls = { "stun:stun4.l.google.com:19302" } }
                        }
                };
                await pc.InitializeAsync(config);
                Console.WriteLine("Peer connection initialized.");

                int numFrames = 0;
                int numAudio = 0;

                // Record video from local webcam, and send to remote peer
                if (needVideo)
                {
                    Console.WriteLine("Opening local webcam...");
                    localVideoTrack = await LocalVideoTrack.CreateFromDeviceAsync();

                    localVideoTrack.I420AVideoFrameReady += (I420AVideoFrame frame) =>
                    {
                        ++numFrames;
                        if (numFrames % 60 == 0)
                        {
                            //Console.WriteLine($"Local video frames: {numFrames}");
                        }
                    };

                    videoTransceiver = pc.AddTransceiver(MediaKind.Video);
                    videoTransceiver.DesiredDirection = Transceiver.Direction.SendReceive;
                    videoTransceiver.LocalVideoTrack = localVideoTrack;
                }
                else
                {

                }

                // Record audio from local microphone, and send to remote peer
                if (needAudio)
                {
                    
                    Console.WriteLine($"Opening local microphone...");
                    var externalSource = ExternalAudioTrackSource.CreateFromCallback(AudioCallback);
                    localAudioTrack = LocalAudioTrack.CreateFromExternalSource("my_track", externalSource);

                    //localAudioTrack = await LocalAudioTrack.CreateFromDeviceAsync();

                    //localAudioTrack.AudioFrameReady += (AudioFrame frame) =>
                    //{
                    //    if (numAudio == 0)
                    //    {
                    //        Console.WriteLine($"Local audio frames: 123");

                    //    }
                    //};

                    audioTransceiver = pc.AddTransceiver(MediaKind.Audio);
                    audioTransceiver.DesiredDirection = Transceiver.Direction.SendReceive;
                    audioTransceiver.LocalAudioTrack = localAudioTrack;

                }

                // Setup signaling
                Console.WriteLine("Starting signaling...");
                var signaler = new NamedPipeSignaler.NamedPipeSignaler(pc, "testpipe");
                signaler.SdpMessageReceived += async (SdpMessage message) => {
                    await pc.SetRemoteDescriptionAsync(message);
                    if (message.Type == SdpMessageType.Offer)
                    {
                        pc.CreateAnswer();
                    }
                };
                signaler.IceCandidateReceived += (IceCandidate candidate) => {
                    pc.AddIceCandidate(candidate);
                };
                await signaler.StartAsync();
                Console.CancelKeyPress += (sender, args) => { 
                    signaler.Stop();
                    pc.Close();
                };

                // Start peer connection
                pc.RenegotiationNeeded += () => {
                    pc.CreateOffer();
                };
                pc.Connected += () => { 
                    Console.WriteLine("PeerConnection: connected.");
                    // Create a new form to display the video feed from the WebRTC peer.
                    
                };
                pc.IceStateChanged += (IceConnectionState newState) => { Console.WriteLine($"ICE state: {newState}"); };
                
                pc.VideoTrackAdded += (RemoteVideoTrack track) =>
                {
                    Console.WriteLine($"start video frames: {numFrames}");

                    //track.I420AVideoFrameReady += (I420AVideoFrame frame) =>
                    //{
                    //    ++numFrames;
                    //    if (numFrames % 60 == 0)
                    //    {
                    //        Console.WriteLine($"Received video frames: {frame.height} {frame.width} {numFrames}");
                    //    }
                    //};
                };

                if (signaler.IsClient)
                {
                    Console.WriteLine("Connecting to remote peer...");
                    pc.CreateOffer();

                    Console.WriteLine("Press a key to stop recording...");
                    Console.ReadKey(true);
                }
                else
                {
                    Console.WriteLine("Waiting for offer from remote peer...");
                    var form = new Form();
                    form.AutoSize = true;
                    form.BackgroundImageLayout = ImageLayout.Center;
                    PictureBox picBox = null;

                    form.HandleDestroyed += (object sender, EventArgs e) =>
                    {
                        Console.WriteLine("Form closed, closing peer connection.");
                        signaler.Stop();
                        pc.Close();
                    };

                    pc.VideoTrackAdded += (RemoteVideoTrack track) =>
                    {
                        track.Argb32VideoFrameReady += (frame) =>
                        {
                            var width = frame.width;
                            var height = frame.height;
                            var stride = frame.stride;
                            var data = frame.data;

                            if (picBox == null)
                            {
                                picBox = new PictureBox
                                {
                                    Size = new Size((int)width, (int)height),
                                    Location = new Point(0, 0),
                                    Visible = true
                                };
                                form.BeginInvoke(new Action(() => { form.Controls.Add(picBox); }));
                            }

                            form.BeginInvoke(new Action(() =>
                            {
                                System.Drawing.Bitmap bmpImage = new System.Drawing.Bitmap((int)width, (int)height, (int)stride, System.Drawing.Imaging.PixelFormat.Format32bppArgb, data);
                                picBox.Image = bmpImage;
                            }));
                        };
                    };

                    Application.EnableVisualStyles();
                    Application.Run(form);
                }

                signaler.Stop();
                pc.Close();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }

            Console.WriteLine("Program termined.");
        }
    }
}
