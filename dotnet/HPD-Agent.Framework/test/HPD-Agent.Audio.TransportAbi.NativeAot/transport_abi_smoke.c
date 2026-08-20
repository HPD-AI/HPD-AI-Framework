#include "hpd_audio_transport.h"
#include <assert.h>

int main(void) {
  int32_t handle = hpd_audio_transport_create(41, 7);
  assert(handle > 0);
  assert(hpd_audio_transport_bind(handle, 41, 8) == -4);
  assert(hpd_audio_transport_bind(handle, 41, 7) == 0);
  assert(hpd_audio_transport_bind(handle, 41, 7) == -5);
  assert(hpd_audio_transport_start(handle, 41, 7) == 0);
  assert(hpd_audio_transport_stop(handle, 41, 7) == 0);
  assert(hpd_audio_transport_destroy(handle, 41, 7) == 0);
  assert(hpd_audio_transport_destroy(handle, 41, 7) == -3);
  return 0;
}
