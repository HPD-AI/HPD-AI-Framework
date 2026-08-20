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
  const uint8_t artifact[]={0xa3,0x01,0x01,0x02,0x43,0xa1,0x01,0x01,0x03,0x58,0x20,0xc0,0x55,0x85,0xb6,0x95,0xc0,0xcf,0x13,0xd9,0x74,0x59,0xcc,0x96,0xa1,0x47,0x58,0x4d,0x9e,0x25,0x34,0x83,0x28,0xd8,0x64,0x69,0xcd,0x31,0xf8,0x61,0x27,0x58,0x43};
  const uint8_t advance_req[]={0xa2,0x01,0x01,0x02,0x43,0xa1,0x01,0x02},step_req[]={0xa2,0x01,0x02,0x02,0x43,0xa1,0x01,0x02},explore_req[]={0xa2,0x01,0x03,0x02,0x43,0xa1,0x01,0x02};
  hpd_replay_handle_v1 h=0;hpd_result_v1 r={0};
  if(open(artifact,sizeof artifact,&h)||!h)return 1;
  if(advance(h,advance_req,sizeof advance_req)||step(h,step_req,sizeof step_req))return 2;
  if(explore(h,explore_req,sizeof explore_req,&r)||r.payload.len!=88)return 3;free(&r.payload);
  if(complete(h,&r)||r.payload.len!=88)return 4;free(&r.payload);
  if(advance(h,advance_req,sizeof advance_req)!=17||close(h)||status(h,&r)!=16)return 5;
  FreeLibrary(module);return 0;
}
