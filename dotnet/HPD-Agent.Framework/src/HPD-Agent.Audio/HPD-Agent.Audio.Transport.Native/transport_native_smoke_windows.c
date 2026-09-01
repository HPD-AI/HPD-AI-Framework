#include "hpd_audio_transport_v1.h"
#include <windows.h>
#include <stdint.h>
#include <stdio.h>
typedef int32_t (__cdecl *create_fn)(uint64_t, uint64_t);
typedef int32_t (__cdecl *transition_fn)(int32_t, uint64_t, uint64_t);
#define LOAD(name, type) type name = (type)GetProcAddress(module, "hpd_audio_transport_v1_" #name); if (!(name)) return 20
#define CHECK(actual, expected, code) do { int32_t value = (actual); if (value != (expected)) { fprintf(stderr, "check %d: got %d expected %d\n", (code), value, (expected)); return (code); } } while (0)
int main(void) {
  HMODULE module = LoadLibraryW(L"HPD-Agent.Audio.Transport.Native.dll");
  if (!module) return 19;
  LOAD(create, create_fn); LOAD(bind, transition_fn); LOAD(start, transition_fn); LOAD(stop, transition_fn); LOAD(destroy, transition_fn);
  int32_t handle = create(41, 7);
  if (handle <= 0) return 10;
  CHECK(bind(handle, 41, 8), -4, 11); CHECK(bind(handle, 41, 7), 0, 12); CHECK(bind(handle, 41, 7), -5, 13);
  CHECK(start(handle, 41, 7), 0, 14); CHECK(stop(handle, 41, 7), 0, 15); CHECK(destroy(handle, 41, 7), 0, 16); CHECK(destroy(handle, 41, 7), -3, 17);
  FreeLibrary(module); return 0;
}
