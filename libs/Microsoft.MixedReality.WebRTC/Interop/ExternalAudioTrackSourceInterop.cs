// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Runtime.InteropServices;

namespace Microsoft.MixedReality.WebRTC.Interop
{
    /// <summary>
    /// Handle to a native external video track source object.
    /// </summary>
    internal sealed class ExternalAudioTrackSourceHandle : SafeHandle
    {
        /// <summary>
        /// Check if the current handle is invalid, which means it is not referencing
        /// an actual native object. Note that a valid handle only means that the internal
        /// handle references a native object, but does not guarantee that the native
        /// object is still accessible. It is only safe to access the native object if
        /// the handle is not closed, which implies it being valid.
        /// </summary>
        public override bool IsInvalid
        {
            get
            {
                return (handle == IntPtr.Zero);
            }
        }

        /// <summary>
        /// Default constructor for an invalid handle.
        /// </summary>
        public ExternalAudioTrackSourceHandle() : base(IntPtr.Zero, ownsHandle: true)
        {
        }

        /// <summary>
        /// Constructor for a valid handle referencing the given native object.
        /// </summary>
        /// <param name="handle">The valid internal handle to the native object.</param>
        public ExternalAudioTrackSourceHandle(IntPtr handle) : base(IntPtr.Zero, ownsHandle: true)
        {
            SetHandle(handle);
        }

        /// <summary>
        /// Release the native object while the handle is being closed.
        /// </summary>
        /// <returns>Return <c>true</c> if the native object was successfully released.</returns>
        protected override bool ReleaseHandle()
        {
            ExternalAudioTrackSourceInterop.ExternalAudioTrackSource_RemoveRef(handle);
            return true;
        }
    }

    internal class ExternalAudioTrackSourceInterop
    {
        #region Unmanaged delegates

        // Note - Those methods cannot use SafeHandle with reverse P/Invoke; use IntPtr instead.

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public unsafe delegate void RequestExternalAudioFrameCallback(IntPtr userData,
            /*ExternalAudioTrackSourceHandle*/IntPtr sourceHandle, uint requestId, long timestampMs);

        [MonoPInvokeCallback(typeof(RequestExternalAudioFrameCallback))]
        public static void RequestAudioFrameFromExternalSourceCallback(IntPtr userData,
            /*ExternalAudioTrackSourceHandle*/IntPtr sourceHandle, uint requestId, long timestampMs)
        {
            var args = Utils.ToWrapper<AudioFrameRequestCallbackArgs>(userData);
            var request = new AudioFrameRequest
            {
                Source = args.Source,
                RequestId = requestId,
                TimestampMs = timestampMs
            };
            args.FrameRequestCallback.Invoke(request);
        }

        #endregion

        public class AudioFrameRequestCallbackArgs
        {
            public ExternalAudioTrackSource Source;
            public AudioFrameRequestDelegate FrameRequestCallback;
            public RequestExternalAudioFrameCallback TrampolineCallback; // keep delegate alive
        }


        #region Native functions

        [DllImport(Utils.dllPath, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi,
            EntryPoint = "mrsExternalAudioTrackSourceCreateFromCallback")]
        public static unsafe extern uint ExternalAudioTrackSource_CreateFromCallback(
            RequestExternalAudioFrameCallback callback, IntPtr userData, out ExternalAudioTrackSourceHandle sourceHandle);

        [DllImport(Utils.dllPath, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi,
            EntryPoint = "mrsExternalAudioTrackSourceFinishCreation")]
        public static unsafe extern uint ExternalAudioTrackSource_FinishCreation(ExternalAudioTrackSourceHandle sourceHandle);

        [DllImport(Utils.dllPath, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi,
            EntryPoint = "mrsExternalAudioTrackSourceAddRef")]
        public static unsafe extern void ExternalAudioTrackSource_AddRef(ExternalAudioTrackSourceHandle handle);

        // Note - This is used during SafeHandle.ReleaseHandle(), so cannot use ExternalAudioTrackSourceHandle
        [DllImport(Utils.dllPath, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi,
            EntryPoint = "mrsExternalAudioTrackSourceRemoveRef")]
        public static unsafe extern void ExternalAudioTrackSource_RemoveRef(IntPtr handle);

        [DllImport(Utils.dllPath, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi,
            EntryPoint = "mrsExternalAudioTrackSourceCompleteFrameRequest")]
        public static unsafe extern uint ExternalAudioTrackSource_CompleteFrameRequest(ExternalAudioTrackSourceHandle handle,
            uint requestId, long timestampMs, in AudioFrame frame);

        [DllImport(Utils.dllPath, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi,
            EntryPoint = "mrsExternalAudioTrackSourceShutdown")]
        public static extern void ExternalAudioTrackSource_Shutdown(ExternalAudioTrackSourceHandle handle);

        #endregion


        #region Helpers

        public static ExternalAudioTrackSource CreateExternalAudioTrackSourceFromCallback(AudioFrameRequestDelegate frameRequestCallback)
        {
            // Create some static callback args which keep the sourceDelegate alive
            var args = new AudioFrameRequestCallbackArgs
            {
                Source = null, // set below
                FrameRequestCallback = frameRequestCallback,
                // This wraps the method into a temporary System.Delegate object, which is then assigned to
                // the field to ensure it is kept alive. The native callback registration below then use that
                // particular System.Delegate instance.
                TrampolineCallback = RequestAudioFrameFromExternalSourceCallback
            };
            var argsRef = Utils.MakeWrapperRef(args);

            try
            {
                // A video track source starts in capturing state, so will immediately call the frame callback,
                // which requires the source to be set. So create the source wrapper first.
                var source = new ExternalAudioTrackSource(argsRef);
                args.Source = source;

                // Create the external video track source
                uint res = ExternalAudioTrackSource_CreateFromCallback(args.TrampolineCallback, argsRef,
                    out ExternalAudioTrackSourceHandle sourceHandle);
                Utils.ThrowOnErrorCode(res);
                source.SetHandle(sourceHandle);
                // Once the handle of the native object is set on the wrapper, notify the implementation to finish
                // the creation, which will make the source start capturing and emit frame requests, which can be
                // handled now that the native handle is known.
                ExternalAudioTrackSource_FinishCreation(sourceHandle);
                return source;
            }
            catch (Exception e)
            {
                Utils.ReleaseWrapperRef(argsRef);
                throw e;
            }
        }

        public static void CompleteFrameRequest(ExternalAudioTrackSourceHandle sourceHandle, uint requestId,
            long timestampMs, in AudioFrame frame)
        {
            uint res = ExternalAudioTrackSource_CompleteFrameRequest(sourceHandle, requestId, timestampMs, frame);
            Utils.ThrowOnErrorCode(res);
        }

        #endregion
    }
}
