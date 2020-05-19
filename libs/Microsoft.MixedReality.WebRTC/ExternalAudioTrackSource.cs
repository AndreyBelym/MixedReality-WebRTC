// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.MixedReality.WebRTC.Interop;

namespace Microsoft.MixedReality.WebRTC
{
    /// <summary>
    /// Request sent to an external video source via its registered callback to generate
    /// a new video frame for the track(s) connected to it.
    /// </summary>
    public ref struct AudioFrameRequest
    {
        /// <summary>
        /// Video track source this request is associated with.
        /// </summary>
        public ExternalAudioTrackSource Source;

        /// <summary>
        /// Unique request identifier, for error checking.
        /// </summary>
        public uint RequestId;

        /// <summary>
        /// Frame timestamp, in milliseconds. This corresponds to the time when the request
        /// was made to the native video track source.
        /// </summary>
        public long TimestampMs;

        /// <summary>
        /// Complete the current request by providing a video frame for it.
        /// This must be used if the video track source was created with
        /// <see cref="ExternalAudioTrackSource.CreateFromCallback(AudioFrameRequestDelegate)"/>.
        /// </summary>
        /// <param name="frame">The video frame used to complete the request.</param>
        public void CompleteRequest(in AudioFrame frame)
        {
            Source.CompleteFrameRequest(RequestId, TimestampMs, frame);
        }
    }

    /// <summary>
    /// Callback invoked when the WebRTC pipeline needs an external video source to generate
    /// a new video frame for the track(s) it is connected to.
    /// </summary>
    /// <param name="request">The request to fulfill with a new I420A video frame.</param>
    public delegate void AudioFrameRequestDelegate(in AudioFrameRequest request);

    /// <summary>
    /// Video source for WebRTC video tracks based on a custom source
    /// of video frames managed by the user and external to the WebRTC
    /// implementation.
    /// 
    /// This class is used to inject into the WebRTC engine a video track
    /// whose frames are produced by a user-managed source the WebRTC engine
    /// knows nothing about, like programmatically generated frames, including
    /// frames not strictly of video origin like a 3D rendered scene, or frames
    /// coming from a specific capture device not supported natively by WebRTC.
    /// This class serves as an adapter for such video frame sources.
    /// </summary>
    public class ExternalAudioTrackSource : IDisposable
    {
        /// <summary>
        /// A name for the external video track source, used for logging and debugging.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// List of local video tracks this source is providing raw video frames to.
        /// </summary>
        public List<LocalAudioTrack> Tracks { get; } = new List<LocalAudioTrack>();

        /// <summary>
        /// Handle to the native ExternalAudioTrackSource object.
        /// </summary>
        /// <remarks>
        /// In native land this is a <code>Microsoft::MixedReality::WebRTC::ExternalAudioTrackSourceHandle</code>.
        /// </remarks>
        internal ExternalAudioTrackSourceHandle _nativeHandle { get; private set; } = new ExternalAudioTrackSourceHandle();

        /// <summary>
        /// GC handle to frame request callback args keeping the delegate alive
        /// while the callback is registered with the native implementation.
        /// </summary>
        protected IntPtr _frameRequestCallbackArgsHandle;

        /// <summary>
        /// Create a new external video track source from a given user callback providing I420A-encoded frames.
        /// </summary>
        /// <param name="frameCallback">The callback that will be used to request frames for tracks.</param>
        /// <returns>The newly created track source.</returns>
        public static ExternalAudioTrackSource CreateFromCallback(AudioFrameRequestDelegate frameCallback)
        {
            return ExternalAudioTrackSourceInterop.CreateExternalAudioTrackSourceFromCallback(frameCallback);
        }

        internal ExternalAudioTrackSource(IntPtr frameRequestCallbackArgsHandle)
        {
            _frameRequestCallbackArgsHandle = frameRequestCallbackArgsHandle;
        }

        internal void SetHandle(ExternalAudioTrackSourceHandle nativeHandle)
        {
            _nativeHandle = nativeHandle;
        }

        /// <summary>
        /// Complete the current request by providing a video frame for it.
        /// This must be used if the video track source was created with
        /// <see cref="CreateFromCallback(AudioFrameRequestDelegate)"/>.
        /// </summary>
        /// <param name="requestId">The original request ID.</param>
        /// <param name="timestampMs">The video frame timestamp.</param>
        /// <param name="frame">The video frame used to complete the request.</param>
        public void CompleteFrameRequest(uint requestId, long timestampMs, in AudioFrame frame)
        {
            ExternalAudioTrackSourceInterop.CompleteFrameRequest(_nativeHandle, requestId, timestampMs, frame);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_nativeHandle.IsClosed)
            {
                return;
            }

            // Unregister and release the track callbacks
            ExternalAudioTrackSourceInterop.ExternalAudioTrackSource_Shutdown(_nativeHandle);
            Utils.ReleaseWrapperRef(_frameRequestCallbackArgsHandle);

            // Destroy the native object. This may be delayed if a P/Invoke callback is underway,
            // but will be handled at some point anyway, even if the managed instance is gone.
            _nativeHandle.Dispose();
        }

        internal void OnTrackAddedToSource(LocalAudioTrack track)
        {
            Debug.Assert(!_nativeHandle.IsClosed);
            Debug.Assert(!Tracks.Contains(track));
            Tracks.Add(track);
        }

        internal void OnTrackRemovedFromSource(LocalAudioTrack track)
        {
            Debug.Assert(!_nativeHandle.IsClosed);
            bool removed = Tracks.Remove(track);
            Debug.Assert(removed);
        }

        internal void OnTracksRemovedFromSource(List<LocalAudioTrack> tracks)
        {
            Debug.Assert(!_nativeHandle.IsClosed);
            var remainingTracks = new List<LocalAudioTrack>();
            foreach (var track in tracks)
            {
                if (track.Source == this)
                {
                    bool removed = Tracks.Remove(track);
                    Debug.Assert(removed);
                }
                else
                {
                    remainingTracks.Add(track);
                }
            }
            tracks = remainingTracks;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"(ExternalAudioTrackSource)\"{Name}\"";
        }
    }
}
