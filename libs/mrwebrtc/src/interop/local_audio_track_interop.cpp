// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// This is a precompiled header, it must be on its own, followed by a blank
// line, to prevent clang-format from reordering it with other headers.
#include "pch.h"

#include "global_factory.h"
#include "local_audio_track_interop.h"
#include "media/external_audio_track_source_impl.h"
#include "media/local_audio_track.h"
#include "utils.h"

using namespace Microsoft::MixedReality::WebRTC;

void MRS_CALL
mrsLocalAudioTrackAddRef(mrsLocalAudioTrackHandle handle) noexcept {
  if (auto track = static_cast<LocalAudioTrack*>(handle)) {
    track->AddRef();
  } else {
    RTC_LOG(LS_WARNING)
        << "Trying to add reference to NULL LocalAudioTrack object.";
  }
}

void MRS_CALL
mrsLocalAudioTrackRemoveRef(mrsLocalAudioTrackHandle handle) noexcept {
  if (auto track = static_cast<LocalAudioTrack*>(handle)) {
    track->RemoveRef();
  } else {
    RTC_LOG(LS_WARNING) << "Trying to remove reference from NULL "
                           "LocalAudioTrack object.";
  }
}

// mrsLocalAudioTrackCreateFromDevice -> interop_api.cpp

mrsResult MRS_CALL mrsLocalAudioTrackCreateFromExternalSource(
    const mrsLocalAudioTrackFromExternalSourceInitConfig* config,
    mrsLocalAudioTrackHandle* track_handle_out) noexcept {
  if (!config || !track_handle_out || !config->source_handle) {
    return Result::kInvalidParameter;
  }
  *track_handle_out = nullptr;

  auto track_source =
      static_cast<detail::ExternalAudioTrackSourceImpl*>(config->source_handle);
  if (!track_source) {
    return Result::kInvalidNativeHandle;
  }

  std::string track_name_str;
  if (!IsStringNullOrEmpty(config->track_name)) {
    track_name_str = config->track_name;
  } else {
    track_name_str = "external_track";
  }

  RefPtr<GlobalFactory> global_factory(GlobalFactory::InstancePtr());
  auto pc_factory = global_factory->GetPeerConnectionFactory();
  if (!pc_factory) {
    return Result::kUnknownError;
  }

  // The audio track keeps a reference to the audio source; let's hope this
  // does not change, because this is not explicitly mentioned in the docs,
  // and the audio track is the only one keeping the audio source alive.
  rtc::scoped_refptr<webrtc::AudioTrackInterface> audio_track =
      pc_factory->CreateAudioTrack(track_name_str, track_source->impl());
  if (!audio_track) {
    return Result::kUnknownError;
  }

  // Create the audio track wrapper
  RefPtr<LocalAudioTrack> track =
      new LocalAudioTrack(std::move(global_factory), std::move(audio_track));
  *track_handle_out = track.release();
  return Result::kSuccess;
}

void MRS_CALL
mrsLocalAudioTrackRegisterFrameCallback(mrsLocalAudioTrackHandle trackHandle,
                                        mrsAudioFrameCallback callback,
                                        void* user_data) noexcept {
  if (auto track = static_cast<LocalAudioTrack*>(trackHandle)) {
    track->SetCallback(AudioFrameReadyCallback{callback, user_data});
  }
}

mrsResult MRS_CALL
mrsLocalAudioTrackSetEnabled(mrsLocalAudioTrackHandle track_handle,
                             mrsBool enabled) noexcept {
  auto track = static_cast<LocalAudioTrack*>(track_handle);
  if (!track) {
    return Result::kInvalidParameter;
  }
  track->SetEnabled(enabled != mrsBool::kFalse);
  return Result::kSuccess;
}

mrsBool MRS_CALL
mrsLocalAudioTrackIsEnabled(mrsLocalAudioTrackHandle track_handle) noexcept {
  auto track = static_cast<LocalAudioTrack*>(track_handle);
  if (!track) {
    return mrsBool::kFalse;
  }
  return (track->IsEnabled() ? mrsBool::kTrue : mrsBool::kFalse);
}
