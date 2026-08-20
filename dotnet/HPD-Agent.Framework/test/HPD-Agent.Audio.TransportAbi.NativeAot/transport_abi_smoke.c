#include "hpd_audio_transport.h"
#include <stdio.h>

#define CHECK(actual, expected, code) do { int32_t value = (actual); if (value != (expected)) { fprintf(stderr, "check %d: got %d expected %d\n", (code), value, (expected)); return (code); } } while (0)

int main(void) {
  int32_t handle = hpd_audio_transport_create(41, 7);
  if (handle <= 0) return 10;
  CHECK(hpd_audio_transport_bind(handle, 41, 8), -4, 11);
  CHECK(hpd_audio_transport_bind(handle, 41, 7), 0, 12);
  CHECK(hpd_audio_transport_bind(handle, 41, 7), -5, 13);
  CHECK(hpd_audio_transport_start(handle, 41, 7), 0, 14);
  CHECK(hpd_audio_transport_stop(handle, 41, 7), 0, 15);
  CHECK(hpd_audio_transport_destroy(handle, 41, 7), 0, 16);
  CHECK(hpd_audio_transport_destroy(handle, 41, 7), -3, 17);
  return 0;
}
