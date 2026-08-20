#ifndef HPD_AUDIO_TRANSPORT_H
#define HPD_AUDIO_TRANSPORT_H
#include <stdint.h>
#ifdef __cplusplus
extern "C" {
#endif
int32_t hpd_audio_transport_create(uint64_t session, uint64_t generation);
int32_t hpd_audio_transport_bind(int32_t handle, uint64_t session, uint64_t generation);
int32_t hpd_audio_transport_start(int32_t handle, uint64_t session, uint64_t generation);
int32_t hpd_audio_transport_stop(int32_t handle, uint64_t session, uint64_t generation);
int32_t hpd_audio_transport_destroy(int32_t handle, uint64_t session, uint64_t generation);
#ifdef __cplusplus
}
#endif
#endif
