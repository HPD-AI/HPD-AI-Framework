#ifndef HPD_AUDIO_TRANSPORT_V1_H
#define HPD_AUDIO_TRANSPORT_V1_H
#include <stdint.h>
#if defined(_WIN32)
#define HPD_CALL __cdecl
#else
#define HPD_CALL
#endif
#ifdef __cplusplus
extern "C" {
#endif
int32_t HPD_CALL hpd_audio_transport_v1_create(uint64_t session, uint64_t generation);
int32_t HPD_CALL hpd_audio_transport_v1_bind(int32_t handle, uint64_t session, uint64_t generation);
int32_t HPD_CALL hpd_audio_transport_v1_start(int32_t handle, uint64_t session, uint64_t generation);
int32_t HPD_CALL hpd_audio_transport_v1_stop(int32_t handle, uint64_t session, uint64_t generation);
int32_t HPD_CALL hpd_audio_transport_v1_destroy(int32_t handle, uint64_t session, uint64_t generation);
#ifdef __cplusplus
}
#endif
#endif
