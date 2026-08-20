#include "hpd_replay_v1.h"
#include <stdint.h>
int main(void){
  const uint8_t artifact[]={0xa1,0x01,0x01},op[]={0xa1,0x02,0x01};
  hpd_replay_handle_v1 h=0;hpd_result_v1 r={0};
  if(hpd_replay_v1_open(artifact,sizeof artifact,&h)||!h)return 1;
  if(hpd_replay_v1_advance(h,op,sizeof op))return 2;
  if(hpd_replay_v1_step(h,op,sizeof op))return 3;
  if(hpd_replay_v1_explore(h,op,sizeof op,&r)||r.payload.len!=88)return 4;
  hpd_replay_v1_free(&r.payload);
  if(hpd_replay_v1_complete(h,&r)||r.payload.len!=88)return 5;
  hpd_replay_v1_free(&r.payload);
  if(hpd_replay_v1_advance(h,op,sizeof op)!=17)return 6;
  if(hpd_replay_v1_close(h))return 7;
  if(hpd_replay_v1_status(h,&r)!=16)return 8;
  return 0;
}
