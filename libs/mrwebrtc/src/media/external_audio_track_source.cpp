// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#include "pch.h"

#include "interop/global_factory.h"
#include "media/external_audio_track_source_impl.h"

namespace {

using namespace Microsoft::MixedReality::WebRTC;

enum {
  /// Request a new video frame from the source.
  MSG_REQUEST_FRAME
};

}  // namespace

namespace Microsoft::MixedReality::WebRTC {
namespace detail {

constexpr const size_t kMaxPendingRequestCount = 64;

RefPtr<ExternalAudioTrackSource> ExternalAudioTrackSourceImpl::create(
    RefPtr<GlobalFactory> global_factory,
    RefPtr<ExternalAudioSource> audio_source) {
  auto source = new ExternalAudioTrackSourceImpl(std::move(global_factory),
                                                 std::move(audio_source));
  // Note: Video track sources always start already capturing; there is no
  // start/stop mechanism at the track level in WebRTC. A source is either being
  // initialized, or is already live. However because of wrappers and interop
  // this step is delayed until |FinishCreation()| is called by the wrapper.
  return source;
}

ExternalAudioTrackSourceImpl::ExternalAudioTrackSourceImpl(
    RefPtr<GlobalFactory> global_factory,
    RefPtr<ExternalAudioSource> audio_source)
    : ExternalAudioTrackSource(std::move(global_factory)),
      audio_source(std::move(audio_source)),
      track_source_(new rtc::RefCountedObject<CustomAudioTrackSourceAdapter>()),
      capture_thread_(rtc::Thread::Create()) {
  capture_thread_->SetName("ExternalAudioTrackSource capture thread", this);
}

ExternalAudioTrackSourceImpl::~ExternalAudioTrackSourceImpl() {
  StopCapture();
}

void ExternalAudioTrackSourceImpl::FinishCreation() {
  StartCapture();
}

void ExternalAudioTrackSourceImpl::StartCapture() {
  // Check if |Shutdown()| was called, in which case the source cannot restart.

  // Start capture thread
  track_source_->state_ = SourceState::kLive;
  pending_requests_.clear();
  capture_thread_->Start();

  // Schedule first frame request for 10ms from now
  int64_t now = rtc::TimeMillis();
  capture_thread_->PostAt(RTC_FROM_HERE, now + 10, this, MSG_REQUEST_FRAME);
}

Result ExternalAudioTrackSourceImpl::CompleteRequest(
    uint32_t request_id,
    int64_t timestamp_ms,
    const AudioFrame& frame_view) {
  // Validate pending request ID and retrieve frame timestamp
  int64_t timestamp_ms_original = -1;
  {
    rtc::CritScope lock(&request_lock_);
    for (auto it = pending_requests_.begin(); it != pending_requests_.end();
         ++it) {
      if (it->first == request_id) {
        timestamp_ms_original = it->second;
        // Remove outdated requests, including current one
        ++it;
        pending_requests_.erase(pending_requests_.begin(), it);
        break;
      }
    }
    if (timestamp_ms_original < 0) {
      return Result::kInvalidParameter;
    }
  }

  // Apply user override if any
  if (timestamp_ms != timestamp_ms_original) {
    timestamp_ms = timestamp_ms_original;
  }

  // Create and dispatch the video frame
  track_source_->DispatchFrame(frame_view);
  return Result::kSuccess;
}

void ExternalAudioTrackSourceImpl::StopCapture() {
  if (track_source_->state_ != SourceState::kEnded) {
    capture_thread_->Stop();
    track_source_->state_ = SourceState::kEnded;
  }
  pending_requests_.clear();
}

void ExternalAudioTrackSourceImpl::Shutdown() noexcept {
  StopCapture();
}

// Note - This is called on the capture thread only.
void ExternalAudioTrackSourceImpl::OnMessage(rtc::Message* message) {
  switch (message->message_id) {
    case MSG_REQUEST_FRAME:
      const int64_t now = rtc::TimeMillis();

      // Request a frame from the external video source
      uint32_t request_id = 0;
      {
        rtc::CritScope lock(&request_lock_);
        // Discard an old request if no space available. This allows restarting
        // after a long delay, otherwise skipping the request generally also
        // prevent the user from calling CompleteFrame() to make some space for
        // more. The queue is still useful for just-in-time or short delays.
        if (pending_requests_.size() >= kMaxPendingRequestCount) {
          pending_requests_.erase(pending_requests_.begin());
        }
        request_id = next_request_id_++;
        pending_requests_.emplace_back(request_id, now);
      }

      AudioFrameRequest request{*this, now, request_id};
      audio_source->FrameRequested(request);

      // Schedule a new request for 30ms from now
      //< TODO - this is unreliable and prone to drifting; figure out something
      // better
      capture_thread_->PostAt(RTC_FROM_HERE, now + 30, this, MSG_REQUEST_FRAME);
      break;
  }
}

}  // namespace detail

ExternalAudioTrackSource::ExternalAudioTrackSource(
    RefPtr<GlobalFactory> global_factory)
    : TrackedObject(std::move(global_factory),
                    ObjectType::kExternalAudioTrackSource) {}

RefPtr<ExternalAudioTrackSource> ExternalAudioTrackSource::create(
    RefPtr<GlobalFactory> global_factory,
    RefPtr<ExternalAudioSource> audio_source) {
  return detail::ExternalAudioTrackSourceImpl::create(
      std::move(global_factory),
      std::move(audio_source));
}

Result AudioFrameRequest::CompleteRequest(
    const AudioFrame& frame_view) {
  auto impl =
      static_cast<detail::ExternalAudioTrackSourceImpl*>(&track_source_);
  return impl->CompleteRequest(request_id_, timestamp_ms_, frame_view);
}
}  // namespace Microsoft::MixedReality::WebRTC
