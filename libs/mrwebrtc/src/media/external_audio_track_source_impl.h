// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#pragma once

#include <vector>
#include <iostream>
#include "api/mediastreaminterface.h"
#include "pc/localaudiosource.h"
#include "callback.h"
#include "external_audio_track_source.h"
#include "interop_api.h"

namespace Microsoft::MixedReality::WebRTC::detail {

/// Adapter to bridge a audio track source to the underlying core
/// implementation.
struct CustomAudioTrackSourceAdapter : public webrtc::LocalAudioSource {
  rtc::CriticalSection sinks_lock_;

  std::vector<webrtc::AudioTrackSinkInterface*> sinks;
  std::vector<webrtc::ObserverInterface*> observers;
  std::vector<AudioObserver*> audioObservers;

  void DispatchFrame(const AudioFrame& frame) { 
      rtc::CritScope cs(&sinks_lock_);

      try {
        for (auto& v : sinks) {
          v->OnData(frame.data_, frame.bits_per_sample_,
                    frame.sampling_rate_hz_, frame.channel_count_,
                    frame.sample_count_);
        }          
      } catch (...) {
        std::cout << "hell";
      }
      
  }

  virtual void SetVolume(double volume) { 
      for (auto& v : audioObservers)
        v->OnSetVolume(volume);
  }

  // Registers/unregisters observers to the audio source.
  virtual void RegisterAudioObserver(AudioObserver* observer) {
    audioObservers.push_back(observer);
  }

  virtual void UnregisterAudioObserver(AudioObserver* observer) {
    auto position =
        std::find(audioObservers.begin(), audioObservers.end(), observer);

    if (position != audioObservers.end())
      audioObservers.erase(position);
  }

  virtual void RegisterObserver(webrtc::ObserverInterface* observer) {
    observers.push_back(observer);
  }

  virtual void UnregisterObserver(webrtc::ObserverInterface* observer) {
    auto position =
        std::find(observers.begin(), observers.end(), observer);

    if (position != observers.end())
      observers.erase(position);
  }

  virtual void AddSink(webrtc::AudioTrackSinkInterface* sink) {
    rtc::CritScope cs(&sinks_lock_);

    sinks.push_back(sink);
  }
  virtual void RemoveSink(webrtc::AudioTrackSinkInterface* sink) {
    rtc::CritScope cs(&sinks_lock_);

    auto position = std::find(sinks.begin(), sinks.end(), sink);

    if (position != sinks.end())
      sinks.erase(position);
  }


  // MediaSourceInterface
  SourceState state() const override { return state_; }
  bool remote() const override { std::cout<<"Test for local"; return false; }

  SourceState state_ = SourceState::kInitializing;
};

/// audio track source acting as an adapter for an external source of raw
/// frames.
class ExternalAudioTrackSourceImpl : public ExternalAudioTrackSource,
                                     public rtc::MessageHandler {
 public:
  using SourceState = webrtc::MediaSourceInterface::SourceState;

  static RefPtr<ExternalAudioTrackSource> create(
      RefPtr<GlobalFactory> global_factory,
      RefPtr<ExternalAudioSource> audio_source);

  ~ExternalAudioTrackSourceImpl() override;

  void SetName(std::string name) { name_ = std::move(name); }
  std::string GetName() const override { return name_; }

  void FinishCreation() override;

  /// Start the audio capture. This will begin to produce audio frames and start
  /// calling the audio frame callback.
  void StartCapture() override;

  /// Complete a audio frame request with a given I420A audio frame.
  Result CompleteRequest(uint32_t request_id,
                         int64_t timestamp_ms,
                         const AudioFrame& frame) override;

  /// Stop the audio capture. This will stop producing audio frames.
  void StopCapture() override;

  /// Shutdown the source and release the buffer adapter and its callback.
  void Shutdown() noexcept override;

  webrtc::AudioSourceInterface* impl() const { return track_source_; }

 protected:
  ExternalAudioTrackSourceImpl(RefPtr<GlobalFactory> global_factory,
                               RefPtr<ExternalAudioSource> audio_source);
  // void Run(rtc::Thread* thread) override;

  void OnMessage(rtc::Message* message) override;

  RefPtr<ExternalAudioSource> audio_source;

  rtc::scoped_refptr<CustomAudioTrackSourceAdapter> track_source_;

  std::unique_ptr<rtc::Thread> capture_thread_;

  /// Collection of pending frame requests
  std::deque<std::pair<uint32_t, int64_t>> pending_requests_
      RTC_GUARDED_BY(request_lock_);  //< TODO : circular buffer to avoid alloc

  /// Next available ID for a frame request.
  uint32_t next_request_id_ RTC_GUARDED_BY(request_lock_){};

  /// Lock for frame requests.
  rtc::CriticalSection request_lock_;

  /// Friendly track source name, for debugging.
  std::string name_;
};

}  // namespace Microsoft::MixedReality::WebRTC::detail
