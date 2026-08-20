#ifndef HPD_HISTORY_V1_H
#define HPD_HISTORY_V1_H
#include <stdint.h>
#if defined(_WIN32)
#define HPD_CALL __cdecl
#else
#define HPD_CALL
#endif
typedef uint64_t hpd_handle_v1;typedef int32_t hpd_status_v1;
typedef struct{uint32_t abi_size,abi_version;const uint8_t*ptr;uint32_t len,reserved;}hpd_bytes_v1;
typedef struct{uint32_t abi_size,abi_version;uint8_t*ptr;uint32_t capacity,written,required;uint64_t cursor;}hpd_output_v1;
typedef struct{uint32_t abi_size,abi_version;int32_t code;uint32_t detail_len;uint8_t*detail;uint64_t reserved;}hpd_error_v1;
uint32_t HPD_CALL hpd_history_abi_version(void);
hpd_status_v1 HPD_CALL hpd_history_query_open(const hpd_bytes_v1*,hpd_handle_v1*,hpd_error_v1*);hpd_status_v1 HPD_CALL hpd_history_query_next(hpd_handle_v1,hpd_output_v1*,hpd_error_v1*);hpd_status_v1 HPD_CALL hpd_history_query_close(hpd_handle_v1,hpd_error_v1*);
hpd_status_v1 HPD_CALL hpd_history_subscription_open(const hpd_bytes_v1*,hpd_handle_v1*,hpd_error_v1*);hpd_status_v1 HPD_CALL hpd_history_subscription_next(hpd_handle_v1,uint32_t,hpd_output_v1*,hpd_error_v1*);hpd_status_v1 HPD_CALL hpd_history_subscription_ack(hpd_handle_v1,uint64_t,hpd_error_v1*);hpd_status_v1 HPD_CALL hpd_history_subscription_close(hpd_handle_v1,hpd_error_v1*);
hpd_status_v1 HPD_CALL hpd_history_export_start(const hpd_bytes_v1*,hpd_handle_v1*,hpd_error_v1*);hpd_status_v1 HPD_CALL hpd_history_export_status(hpd_handle_v1,hpd_output_v1*,hpd_error_v1*);hpd_status_v1 HPD_CALL hpd_history_export_cancel(hpd_handle_v1,hpd_error_v1*);hpd_status_v1 HPD_CALL hpd_history_export_content_open(hpd_handle_v1,hpd_handle_v1*,hpd_error_v1*);hpd_status_v1 HPD_CALL hpd_history_export_content_next(hpd_handle_v1,hpd_output_v1*,hpd_error_v1*);hpd_status_v1 HPD_CALL hpd_history_export_content_close(hpd_handle_v1,hpd_error_v1*);
hpd_status_v1 HPD_CALL hpd_privacy_delete_start(const hpd_bytes_v1*,hpd_handle_v1*,hpd_error_v1*);hpd_status_v1 HPD_CALL hpd_privacy_hold_start(const hpd_bytes_v1*,hpd_handle_v1*,hpd_error_v1*);hpd_status_v1 HPD_CALL hpd_privacy_hold_release(hpd_handle_v1,const hpd_bytes_v1*,hpd_error_v1*);hpd_status_v1 HPD_CALL hpd_privacy_status(hpd_handle_v1,hpd_output_v1*,hpd_error_v1*);hpd_status_v1 HPD_CALL hpd_privacy_cancel(hpd_handle_v1,hpd_error_v1*);
void HPD_CALL hpd_buffer_free(void*,uint32_t);void HPD_CALL hpd_error_free(hpd_error_v1*);
#endif
