// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#pragma once

#include "external_audio_track_source_interop.h"
#include "mrs_errors.h"
#include "refptr.h"
#include "tracked_object.h"

namespace Microsoft::MixedReality::WebRTC {

class ExternalAudioTrackSource;

/// Frame request for an external audio source producing audio frames encoded in
/// I420 format, with optional Alpha (opacity) plane.
struct AudioFrameRequest {
  /// audio track source the request is related to.
  ExternalAudioTrackSource& track_source_;

  /// audio frame timestamp, in milliseconds.
  std::int64_t timestamp_ms_;

  /// Unique identifier of the request.
  const std::uint32_t request_id_;

  /// Complete the request by making the track source consume the given audio
  /// frame and have it deliver the frame to all its audio tracks.
  Result CompleteRequest(const AudioFrame& frame_view);
};

/// Custom audio source producing audio frames encoded in I420 format, with
/// optional Alpha (opacity) plane.
class ExternalAudioSource : public RefCountedBase {
 public:
  /// Produce a audio frame for a request initiated by an external track source.
  ///
  /// This callback is invoked automatically by the track source whenever a new
  /// audio frame is needed (pull model). The custom audio source implementation
  /// must either return an error, or produce a new audio frame and call the
  /// |CompleteRequest()| request on the |frame_request| object.
  virtual Result FrameRequested(AudioFrameRequest& frame_request) = 0;
};

/// audio track source acting as an adapter for an external source of raw
/// frames.
class ExternalAudioTrackSource : public TrackedObject {
 public:
  /// Helper to create an external audio track source from a custom I420A audio
  /// frame request callback.
  static RefPtr<ExternalAudioTrackSource> create(
      RefPtr<GlobalFactory> global_factory,
      RefPtr<ExternalAudioSource> audio_source);

  /// Finish the creation of the audio track source, and start capturing.
  /// See |mrsExternalaudioTrackSourceFinishCreation()| for details.
  virtual void FinishCreation() = 0;

  /// Start the audio capture. This will begin to produce audio frames and start
  /// calling the audio frame callback.
  virtual void StartCapture() = 0;

  /// Complete a given audio frame request with the provided I420A frame.
  /// The caller must know the source expects an I420A frame; there is no check
  /// to confirm the source is I420A-based or ARGB32-based.
  virtual Result CompleteRequest(uint32_t request_id,
                                 int64_t timestamp_ms,
                                 const AudioFrame& frame) = 0;

  /// Stop the audio capture. This will stop producing audio frames.
  virtual void StopCapture() = 0;

  /// Shutdown the source and release the buffer adapter and its callback.
  virtual void Shutdown() noexcept = 0;

 protected:
  ExternalAudioTrackSource(RefPtr<GlobalFactory> global_factory);
};

namespace detail {

//
// Helpers
//

/// Create an I420A external audio track source wrapping the given interop
/// callback.
RefPtr<ExternalAudioTrackSource> ExternalAudioTrackSourceCreate(
    RefPtr<GlobalFactory> global_factory,
    mrsRequestExternalAudioFrameCallback callback,
    void* user_data);
}  // namespace detail

}  // namespace Microsoft::MixedReality::WebRTC
