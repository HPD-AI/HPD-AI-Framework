#ifndef HPD_AGENT_EVENTS_H
#define HPD_AGENT_EVENTS_H

#include <stddef.h>
#include <stdint.h>

#if defined(_WIN32)
#define HPD_CALL __cdecl
#else
#define HPD_CALL
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef struct hpd_agent hpd_agent;
typedef struct hpd_subscription hpd_subscription;

typedef enum {
    HPD_AGENT_EVENT_EXACT_THREAD = 0,
    HPD_AGENT_EVENT_DIRECT_CHILDREN = 1,
    HPD_AGENT_EVENT_THREAD_AND_DIRECT_CHILDREN = 2,
    HPD_AGENT_EVENT_DESCENDANTS = 3,
    HPD_AGENT_EVENT_THREAD_AND_DESCENDANTS = 4
} hpd_agent_event_hierarchy;

typedef void (HPD_CALL *hpd_event_delivery_callback)(
    const uint8_t* json,
    size_t json_length,
    void* user_data);

typedef enum {
    HPD_SUBSCRIBE_OK = 0,
    HPD_SUBSCRIBE_INVALID_ARGUMENT = 1,
    HPD_SUBSCRIBE_INVALID_UTF8 = 2,
    HPD_SUBSCRIBE_INVALID_HIERARCHY = 3,
    HPD_SUBSCRIBE_DISPOSED_AGENT = 4,
    HPD_SUBSCRIBE_INTERNAL_ERROR = 5
} hpd_subscribe_status;

hpd_subscribe_status hpd_agent_subscribe_events(
    hpd_agent* agent,
    const uint8_t* session_id,
    size_t session_id_length,
    const uint8_t* thread_id,
    size_t thread_id_length,
    int32_t hierarchy,
    hpd_event_delivery_callback callback,
    void* user_data,
    hpd_subscription** out_subscription);

typedef enum {
    HPD_SUBSCRIPTION_DISPOSED = 0,
    HPD_SUBSCRIPTION_DISPOSE_INVALID_ARGUMENT = 1,
    HPD_SUBSCRIPTION_DISPOSE_FROM_CALLBACK = 2
} hpd_subscription_dispose_status;

hpd_subscription_dispose_status hpd_subscription_dispose(
    hpd_subscription** subscription);

#ifdef __cplusplus
}
#endif

#endif
