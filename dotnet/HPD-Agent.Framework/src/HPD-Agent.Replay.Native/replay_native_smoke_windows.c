#include <windows.h>
#include <stdint.h>
#include "hpd_replay_v1.h"
typedef int32_t (__cdecl *open_fn)(const uint8_t*,uint64_t,hpd_replay_handle_v1*);
typedef int32_t (__cdecl *mutate_fn)(hpd_replay_handle_v1,const uint8_t*,uint64_t);
typedef int32_t (__cdecl *result_fn)(hpd_replay_handle_v1,hpd_result_v1*);
typedef int32_t (__cdecl *explore_fn)(hpd_replay_handle_v1,const uint8_t*,uint64_t,hpd_result_v1*);
typedef int32_t (__cdecl *close_fn)(hpd_replay_handle_v1);
typedef void (__cdecl *free_fn)(hpd_owned_bytes_v1*);
#define LOAD(module,name,type) type name=(type)GetProcAddress(module,"hpd_replay_v1_" #name);if(!(name))return 20
int main(void){
  HMODULE module=LoadLibraryA("HPD-Agent.Replay.Native.dll");if(!module)return 19;
  LOAD(module,open,open_fn);LOAD(module,advance,mutate_fn);LOAD(module,step,mutate_fn);
  LOAD(module,explore,explore_fn);LOAD(module,status,result_fn);LOAD(module,complete,result_fn);
  LOAD(module,close,close_fn);LOAD(module,free,free_fn);
  const uint8_t artifact[]={0xa1,0x01,0x01},op[]={0xa1,0x02,0x01};
  hpd_replay_handle_v1 h=0;hpd_result_v1 r={0};
  if(open(artifact,sizeof artifact,&h)||!h)return 1;
  if(advance(h,op,sizeof op)||step(h,op,sizeof op))return 2;
  if(explore(h,op,sizeof op,&r)||r.payload.len!=88)return 3;free(&r.payload);
  if(complete(h,&r)||r.payload.len!=88)return 4;free(&r.payload);
  if(advance(h,op,sizeof op)!=17||close(h)||status(h,&r)!=16)return 5;
  FreeLibrary(module);return 0;
}
