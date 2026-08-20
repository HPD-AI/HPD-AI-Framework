#ifndef HPD_REPLAY_V1_H
#define HPD_REPLAY_V1_H
#include <stdint.h>
#if defined(_WIN32)
#define HPD_CALL __cdecl
#else
#define HPD_CALL
#endif
typedef uint64_t hpd_replay_handle_v1;
typedef struct { uint8_t* ptr; uint64_t len; uint64_t cap; } hpd_owned_bytes_v1;
typedef struct { int32_t code; hpd_owned_bytes_v1 payload; hpd_owned_bytes_v1 error; } hpd_result_v1;
int32_t HPD_CALL hpd_replay_v1_open(const uint8_t*,uint64_t,hpd_replay_handle_v1*);
int32_t HPD_CALL hpd_replay_v1_advance(hpd_replay_handle_v1,const uint8_t*,uint64_t);
int32_t HPD_CALL hpd_replay_v1_step(hpd_replay_handle_v1,const uint8_t*,uint64_t);
int32_t HPD_CALL hpd_replay_v1_explore(hpd_replay_handle_v1,const uint8_t*,uint64_t,hpd_result_v1*);
int32_t HPD_CALL hpd_replay_v1_status(hpd_replay_handle_v1,hpd_result_v1*);
int32_t HPD_CALL hpd_replay_v1_complete(hpd_replay_handle_v1,hpd_result_v1*);
int32_t HPD_CALL hpd_replay_v1_close(hpd_replay_handle_v1);
void HPD_CALL hpd_replay_v1_free(hpd_owned_bytes_v1*);
#endif
